using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Mock;

/// <summary>
/// wizMES API Mock 서버 (HTTP :47300).
/// [MOCK] 실제 서버가 아닙니다. 테스트 전용.
/// </summary>
public sealed class MockApiServer
{
    private readonly int _port;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── db.json 로드 ──────────────────────────────────────────────────────────

    private sealed class DbData
    {
        [JsonPropertyName("mock_users")]
        public List<DbUser> Users { get; init; } = [];
        [JsonPropertyName("mock_products")]
        public List<DbProduct> Products { get; init; } = [];
        [JsonPropertyName("mock_lots")]
        public List<DbLot> Lots { get; init; } = [];
        [JsonPropertyName("mock_materials")]
        public List<DbMaterial> Materials { get; init; } = [];
        [JsonPropertyName("mock_issue_results")]
        public List<DbIssueResult> IssueResults { get; init; } = [];
        [JsonPropertyName("mock_injection_conditions")]
        public List<DbInjCond> InjectionConditions { get; init; } = [];
    }

    private sealed class DbIssueResult
    {
        [JsonPropertyName("id")]               public int    Id               { get; init; }
        [JsonPropertyName("materialBarcode")]  public string MaterialBarcode  { get; init; } = "";
        [JsonPropertyName("lotBarcode")]       public string LotBarcode       { get; init; } = "";
        [JsonPropertyName("inspectionResult")] public string InspectionResult { get; init; } = "";
        [JsonPropertyName("savedAt")]          public string SavedAt          { get; init; } = "";
    }

    private sealed class DbUser
    {
        [JsonPropertyName("login_company")] public string LoginCompany { get; init; } = "";
        [JsonPropertyName("login_id")]      public string LoginId      { get; init; } = "";
        [JsonPropertyName("login_password")] public string LoginPassword { get; init; } = "";
    }
    private sealed class DbProduct  { public string ProductCode { get; init; } = ""; public string ProductName { get; init; } = ""; }
    private sealed class DbLot      { public string LotCode { get; init; } = ""; public string ProductCode { get; init; } = ""; public string ManufactureDate { get; init; } = ""; public string ProductionEquipment { get; init; } = ""; public string ProductionMold { get; init; } = ""; public string LotProductionQty { get; init; } = ""; }
    private sealed class DbMaterial { public string MaterialBarcode { get; init; } = ""; public string LotCode { get; init; } = ""; public string ContainerQty { get; init; } = ""; }
    private sealed class DbInjCell  { public string Set { get; init; } = ""; public string Act { get; init; } = ""; }
    private sealed class DbInjCond  { public string LotCode { get; init; } = ""; public string GroupName { get; init; } = ""; public string RowName { get; init; } = ""; public DbInjCell H1 { get; init; } = new(); public DbInjCell H2 { get; init; } = new(); public DbInjCell H3 { get; init; } = new(); public DbInjCell H4 { get; init; } = new(); public DbInjCell H5 { get; init; } = new(); }

    private static (DbData db, string? path) LoadDb()
    {
        // exe 위치 → cwd 순으로 탐색
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        string? path = null;
        if (exeDir is not null) { var p = Path.Combine(exeDir, "db.json"); if (File.Exists(p)) path = p; }
        if (path is null) { var p = Path.Combine(Directory.GetCurrentDirectory(), "db.json"); if (File.Exists(p)) path = p; }

        if (path is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [MOCK API] WARNING: db.json not found. No seed data.");
            Console.ResetColor();
            return (new DbData(), null);
        }

        try
        {
            var db = JsonSerializer.Deserialize<DbData>(File.ReadAllText(path), JsonOpts) ?? new DbData();
            Console.WriteLine($"  [MOCK API] db.json loaded: {db.Products.Count} products, {db.Lots.Count} lots, {db.Materials.Count} materials, {db.IssueResults.Count} issue results");
            return (db, path);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [MOCK API] WARNING: Failed to parse db.json: {ex.Message}");
            Console.ResetColor();
            return (new DbData(), null);
        }
    }

    // ── Seed snapshot (db.json 변경 시 통째로 교체) ───────────────────────────

    private sealed class SeedSnapshot
    {
        public required Dictionary<string, DbProduct>  Products            { get; init; }
        public required Dictionary<string, DbLot>      Lots                { get; init; }
        public required Dictionary<string, DbMaterial> Materials           { get; init; }
        public required List<DbInjCond>                InjectionConditions { get; init; }
        public required (string LoginCompany, string LoginId, string LoginPassword)[] Users { get; init; }

        public static SeedSnapshot From(DbData db) => new()
        {
            Products            = db.Products.ToDictionary(p => p.ProductCode),
            Lots                = db.Lots.ToDictionary(l => l.LotCode),
            Materials           = db.Materials.ToDictionary(m => m.MaterialBarcode),
            InjectionConditions = db.InjectionConditions,
            Users               = db.Users.Count > 0
                ? db.Users.Select(u => (u.LoginCompany, u.LoginId, u.LoginPassword)).ToArray()
                : [("MANTEC", "admin", "1234")],
        };
    }

    private volatile SeedSnapshot _seed;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private record IssueResult(int Id, string MaterialBarcode, string LotBarcode,
        string InspectionResult, string SavedAt);

    private readonly List<IssueResult> _issueResults;
    private int _nextId;
    private readonly HashSet<string> _refreshTokens = [];
    private readonly object _lock = new();

    private DbData _dbData = new();
    private string? _dbPath;
    private long _lastReloadTick;

    private static readonly JsonSerializerOptions SaveOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public MockApiServer(int port)
    {
        _port = port;
        (_dbData, _dbPath) = LoadDb();
        _seed = SeedSnapshot.From(_dbData);

        // issue_results는 db.json에서 로드 (재시작해도 유지)
        _issueResults = _dbData.IssueResults
            .Select(r => new IssueResult(r.Id, r.MaterialBarcode, r.LotBarcode, r.InspectionResult, r.SavedAt))
            .ToList();
        _nextId = _issueResults.Count > 0 ? _issueResults.Max(r => r.Id) + 1 : 1;
    }

    // db.json의 seed 데이터를 다시 읽어 _seed를 교체 (issue_results는 건드리지 않음)
    private void ReloadSeed()
    {
        // 500ms 내 중복 이벤트 무시 (FileSystemWatcher가 여러 번 발화할 수 있음)
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastReloadTick) < TimeSpan.FromMilliseconds(500).Ticks) return;
        Interlocked.Exchange(ref _lastReloadTick, now);

        // 파일이 아직 쓰여지는 중일 수 있으므로 짧게 대기 후 읽기
        Thread.Sleep(100);
        try
        {
            if (_dbPath is null) return;
            var db = JsonSerializer.Deserialize<DbData>(File.ReadAllText(_dbPath), JsonOpts) ?? new DbData();
            _dbData = db;
            _seed   = SeedSnapshot.From(db);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [MOCK API] db.json reloaded: {db.Products.Count} products, {db.Lots.Count} lots, {db.Materials.Count} materials");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [MOCK API] WARNING: Failed to reload db.json: {ex.Message}");
        }
    }

    // mock_issue_results만 업데이트해서 db.json에 저장
    private void SaveIssueResults()
    {
        if (_dbPath is null) return;
        try
        {
            var seed = _seed;
            var toSave = new
            {
                mock_users               = seed.Users.Select(u => new { login_company = u.LoginCompany, login_id = u.LoginId, login_password = u.LoginPassword }),
                mock_products            = seed.Products.Values,
                mock_lots                = seed.Lots.Values,
                mock_materials           = seed.Materials.Values,
                mock_issue_results       = _issueResults.Select(r => new DbIssueResult
                {
                    Id = r.Id, MaterialBarcode = r.MaterialBarcode, LotBarcode = r.LotBarcode,
                    InspectionResult = r.InspectionResult, SavedAt = r.SavedAt,
                }),
                mock_injection_conditions = seed.InjectionConditions,
            };
            File.WriteAllText(_dbPath, JsonSerializer.Serialize(toSave, SaveOpts), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [MOCK API] WARNING: Failed to save db.json: {ex.Message}");
        }
    }

    // ── Server loop ───────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{_port}/");
        listener.Start();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [MOCK API]  http://localhost:{_port}");
        foreach (var u in _seed.Users)
            Console.WriteLine($"              Auth: {u.LoginCompany} / {u.LoginId} / {u.LoginPassword}");
        Console.ResetColor();

        // db.json 실시간 감시
        FileSystemWatcher? watcher = null;
        if (_dbPath is not null)
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath))
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, _) => ReloadSeed();
            Console.WriteLine($"  [MOCK API]  Watching db.json for changes...");
        }

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try   { ctx = await listener.GetContextAsync().WaitAsync(ct); }
            catch { break; }

            _ = Task.Run(() => HandleRequestAsync(ctx), ct);
        }

        listener.Stop();
        watcher?.Dispose();
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var res  = ctx.Response;
        var meth = req.HttpMethod.ToUpperInvariant();
        var path = Uri.UnescapeDataString(req.Url!.AbsolutePath).TrimEnd('/');

        Console.WriteLine($"  [MOCK API] {meth} {path}");

        try
        {
            if      (meth == "POST"   && path == "/api/login")
                await HandleLoginAsync(req, res);
            else if (meth == "POST"   && path == "/api/auth/refresh")
                await HandleRefreshAsync(req, res);
            else if (meth == "GET"    && path.StartsWith("/api/marking/materials/"))
                await HandleGetMaterialAsync(req, res, path["/api/marking/materials/".Length..]);
            else if (meth == "POST"   && path == "/api/marking/issue-results")
                await HandlePostIssueResultsAsync(req, res);
            else if (meth == "GET"    && path == "/api/marking/lots")
                HandleGetLots(req, res);
            else if (meth == "GET"    && path.StartsWith("/api/marking/production-lots/"))
                HandleGetProductionLot(res, path["/api/marking/production-lots/".Length..]);
            else if (meth == "DELETE" && path == "/api/marking/test/reset")
                HandleTestReset(res);
            else
                WriteJson(res, 404, Fail("NOT_FOUND", "Not found"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [MOCK API] Error: {ex.Message}");
            WriteJson(res, 500, Fail("INTERNAL_ERROR", ex.Message));
        }
    }

    // ── POST /api/login ───────────────────────────────────────────────────────

    private async Task HandleLoginAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        var body = await ReadJsonAsync<LoginRequest>(req);
        if (body?.LoginCompany == null || body.LoginId == null || body.LoginPassword == null)
        {
            WriteJson(res, 400, Fail("INVALID_REQUEST", "login_company, login_id, login_password 가 필요합니다."));
            return;
        }

        bool loginOk = _seed.Users.Any(u =>
            u.LoginCompany == body.LoginCompany && u.LoginId == body.LoginId && u.LoginPassword == body.LoginPassword);

        if (!loginOk)
        {
            WriteJson(res, 401, Fail("INVALID_CREDENTIALS", "업체코드·아이디·비밀번호가 올바르지 않습니다."));
            return;
        }

        var accessToken  = "mock-access-"  + Guid.NewGuid().ToString("N")[..12];
        var refreshToken = "mock-refresh-" + Guid.NewGuid().ToString("N")[..12];
        lock (_lock) _refreshTokens.Add(refreshToken);

        Console.WriteLine($"  [MOCK API] Login: {body.LoginCompany}/{body.LoginId}");
        WriteJson(res, 200, new { ok = true, accessToken, refreshToken, expiresIn = 3600, tokenType = "Bearer" });
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────

    private async Task HandleRefreshAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        var body = await ReadJsonAsync<RefreshRequest>(req);
        if (body?.RefreshToken == null)
        {
            WriteJson(res, 400, Fail("INVALID_REQUEST", "refreshToken 이 필요합니다."));
            return;
        }

        lock (_lock)
        {
            if (!_refreshTokens.Remove(body.RefreshToken))
            {
                WriteJson(res, 401, Fail("INVALID_REFRESH_TOKEN", "Refresh Token 이 만료되었거나 유효하지 않습니다."));
                return;
            }
        }

        var newAccess  = "mock-access-"  + Guid.NewGuid().ToString("N")[..12];
        var newRefresh = "mock-refresh-" + Guid.NewGuid().ToString("N")[..12];
        lock (_lock) _refreshTokens.Add(newRefresh);

        Console.WriteLine($"  [MOCK API] Token rotated");
        WriteJson(res, 200, new { ok = true, accessToken = newAccess, refreshToken = newRefresh, expiresIn = 3600, tokenType = "Bearer" });
    }

    // ── GET /api/marking/materials/:materialBarcode ───────────────────────────

    private Task HandleGetMaterialAsync(HttpListenerRequest _, HttpListenerResponse res, string barcode)
    {
        var seed = _seed;  // 스냅샷 고정 (핸들러 실행 중 reload 영향 없음)
        DbMaterial? material;
        bool synthetic = false;

        if (!seed.Materials.TryGetValue(barcode, out material))
        {
            var defaultLot = seed.Lots.Values.FirstOrDefault();
            if (defaultLot is null)
            {
                WriteJson(res, 404, Fail("LOT_NOT_FOUND", "등록된 Lot이 없습니다."));
                return Task.CompletedTask;
            }
            material  = new DbMaterial { MaterialBarcode = barcode, LotCode = defaultLot.LotCode, ContainerQty = "10" };
            synthetic = true;
        }

        if (!seed.Lots.TryGetValue(material.LotCode, out var lot))
        {
            WriteJson(res, 404, Fail("LOT_NOT_FOUND", "연결된 Lot 정보를 찾을 수 없습니다."));
            return Task.CompletedTask;
        }

        if (!seed.Products.TryGetValue(lot.ProductCode, out var product))
        {
            WriteJson(res, 404, Fail("PRODUCT_NOT_FOUND", "연결된 품목 정보를 찾을 수 없습니다."));
            return Task.CompletedTask;
        }

        var matBarcodes = seed.Materials.Values
            .Where(m => m.LotCode == material.LotCode)
            .Select(m => m.MaterialBarcode)
            .ToHashSet();
        if (synthetic) matBarcodes.Add(barcode);

        string lastIssuedSer;
        lock (_lock)
        {
            var lotResults = _issueResults.Where(r => matBarcodes.Contains(r.MaterialBarcode)).ToList();
            lastIssuedSer = lotResults.Count > 0
                ? lotResults.Select(r => r.LotBarcode[^6..]).Max()!
                : "000000";
        }

        Console.WriteLine($"  [MOCK API] Material: {barcode}{(synthetic ? " (synthetic)" : "")} lastSer={lastIssuedSer}");
        WriteJson(res, 200, Ok(new
        {
            materialBarcode     = barcode,
            productName         = product.ProductName,
            manufactureDate     = lot.ManufactureDate,
            containerQty        = material.ContainerQty,
            lotCode             = material.LotCode,
            lastIssuedSer,
            productionEquipment = lot.ProductionEquipment,
            productionMold      = lot.ProductionMold,
            lotProductionQty    = lot.LotProductionQty,
        }));

        return Task.CompletedTask;
    }

    // ── POST /api/marking/issue-results ──────────────────────────────────────

    private async Task HandlePostIssueResultsAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        var body = await ReadJsonAsync<IssueResultsRequest>(req);
        if (body?.MaterialBarcode == null || body.Results == null)
        {
            WriteJson(res, 400, Fail("INVALID_REQUEST", "materialBarcode 와 results 배열이 필요합니다."));
            return;
        }

        var savedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        lock (_lock)
        {
            foreach (var r in body.Results)
                _issueResults.Add(new IssueResult(_nextId++, body.MaterialBarcode, r.LotBarcode, r.InspectionResult, savedAt));
        }
        SaveIssueResults();

        Console.WriteLine($"  [MOCK API] IssueResults: {body.MaterialBarcode} count={body.Results.Count}");
        WriteJson(res, 200, Ok(new { savedCount = body.Results.Count }));
    }

    // ── GET /api/marking/lots ─────────────────────────────────────────────────

    private void HandleGetLots(HttpListenerRequest req, HttpListenerResponse res)
    {
        var qmb = req.QueryString["materialBarcode"];
        var qlc = req.QueryString["lotCode"];

        if (string.IsNullOrEmpty(qmb) && string.IsNullOrEmpty(qlc))
        {
            WriteJson(res, 400, Fail("MISSING_QUERY_PARAM", "materialBarcode 또는 lotCode 파라미터가 필요합니다."));
            return;
        }

        List<IssueResult> results;
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(qmb))
            {
                results = _issueResults.Where(r => r.MaterialBarcode == qmb).ToList();
            }
            else
            {
                var barcodes = _seed.Materials.Values
                    .Where(m => m.LotCode == qlc)
                    .Select(m => m.MaterialBarcode)
                    .ToHashSet();
                results = _issueResults.Where(r => barcodes.Contains(r.MaterialBarcode)).ToList();
            }
        }

        var items = results.Select(r => new
        {
            materialBarcode  = r.MaterialBarcode,
            lotBarcode       = r.LotBarcode,
            inspectionResult = r.InspectionResult,
        }).ToList();

        Console.WriteLine($"  [MOCK API] Lots: query={qmb ?? qlc} → {items.Count}건");
        WriteJson(res, 200, Ok(new { items, totalCount = items.Count }));
    }

    // ── GET /api/marking/production-lots/:lotCode ─────────────────────────────

    private void HandleGetProductionLot(HttpListenerResponse res, string lotCode)
    {
        var seed = _seed;  // 스냅샷 고정
        if (!seed.Lots.TryGetValue(lotCode, out var lot))
        {
            WriteJson(res, 404, Fail("LOT_NOT_FOUND", "해당 Lot 코드를 찾을 수 없습니다."));
            return;
        }

        seed.Products.TryGetValue(lot.ProductCode, out var product);

        var barcodes = seed.Materials.Values
            .Where(m => m.LotCode == lotCode)
            .Select(m => m.MaterialBarcode)
            .ToHashSet();

        int okCount, ngCount;
        lock (_lock)
        {
            var allResults = _issueResults.Where(r => barcodes.Contains(r.MaterialBarcode)).ToList();
            okCount = allResults.Count(r => r.InspectionResult == "OK");
            ngCount = allResults.Count(r => r.InspectionResult == "NG");
        }

        var conditions = seed.InjectionConditions
            .Where(c => c.LotCode == lotCode)
            .Select(c => new
            {
                groupName = c.GroupName,
                rowName   = c.RowName,
                h1 = new { set = c.H1.Set, act = c.H1.Act },
                h2 = new { set = c.H2.Set, act = c.H2.Act },
                h3 = new { set = c.H3.Set, act = c.H3.Act },
                h4 = new { set = c.H4.Set, act = c.H4.Act },
                h5 = new { set = c.H5.Set, act = c.H5.Act },
            })
            .ToList();

        Console.WriteLine($"  [MOCK API] ProductionLot: {lotCode} ok={okCount} ng={ngCount}");
        WriteJson(res, 200, Ok(new
        {
            lotCode,
            productName         = product?.ProductName ?? "",
            manufactureDate     = lot.ManufactureDate,
            productionEquipment = lot.ProductionEquipment,
            productionMold      = lot.ProductionMold,
            lotProductionQty    = lot.LotProductionQty,
            okCount,
            ngCount,
            injectionConditions = conditions,
        }));
    }

    // ── DELETE /api/marking/test/reset ────────────────────────────────────────

    private void HandleTestReset(HttpListenerResponse res)
    {
        int cleared;
        lock (_lock)
        {
            cleared = _issueResults.Count;
            _issueResults.Clear();
            _nextId = 1;
        }
        SaveIssueResults();

        Console.WriteLine($"  [MOCK API] Test reset: cleared={cleared}");
        WriteJson(res, 200, Ok(new { clearedCount = cleared }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object Ok(object data)   => new { success = true,  data, error = (object?)null };
    private static object Fail(string code, string message) =>
        new { success = false, data = (object?)null, error = new { code, message } };

    private static void WriteJson(HttpListenerResponse res, int status, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        res.StatusCode      = status;
        res.ContentType     = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        try { res.OutputStream.Write(bytes); res.Close(); }
        catch { }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return string.IsNullOrEmpty(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOpts);
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    private sealed class LoginRequest
    {
        [JsonPropertyName("login_company")]  public string? LoginCompany  { get; init; }
        [JsonPropertyName("login_id")]       public string? LoginId       { get; init; }
        [JsonPropertyName("login_password")] public string? LoginPassword { get; init; }
    }

    private sealed class RefreshRequest
    {
        public string? RefreshToken { get; init; }
    }

    private sealed class IssueResultItem
    {
        public string LotBarcode       { get; init; } = "";
        public string InspectionResult { get; init; } = "";
    }

    private sealed class IssueResultsRequest
    {
        public string?                MaterialBarcode { get; init; }
        public List<IssueResultItem>? Results         { get; init; }
    }
}
