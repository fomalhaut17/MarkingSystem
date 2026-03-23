using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

var baseUrl      = "https://wizmes.com";
var loginId      = "";
var loginPw      = "";
var loginCompany = "MANTEC";

var http = new HttpClient();
var jsonOpt = new JsonSerializerOptions { WriteIndented = true };

string? accessToken  = null;
string? refreshToken = null;

// ── 로그인 (항상 먼저 실행) ───────────────────────────────────────────────────
await RunLoginAsync();
if (accessToken == null) return;

// ── 메뉴 ─────────────────────────────────────────────────────────────────────
while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== API 선택 ===");
    Console.WriteLine("1. POST /api/login (재로그인)");
    Console.WriteLine("2. POST /api/auth/refresh");
    Console.WriteLine("3. POST /api/mantec/lot/lookup_by_barcode");
    Console.WriteLine("4. POST /api/mantec/lot/save_inspection");
    Console.WriteLine("5. POST /api/mantec/lot/detail_with_equipment");
    Console.WriteLine("0. 종료");
    Console.Write("선택: ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1": await RunLoginAsync();   break;
        case "2": await RunRefreshAsync(); break;
        case "3": await RunLookupAsync();  break;
        case "4": await RunSaveAsync();    break;
        case "5": await RunDetailAsync();  break;
        case "0": return;
        default:  Console.WriteLine("잘못된 입력"); break;
    }
}

// ── 1. 로그인 ─────────────────────────────────────────────────────────────────
async Task RunLoginAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== 1. POST /api/login ===");
    var res  = await http.PostAsJsonAsync($"{baseUrl}/api/login", new
    {
        login_id       = loginId,
        login_password = loginPw,
        login_company  = loginCompany
    });
    var body = await res.Content.ReadAsStringAsync();
    PrintResponse(res, body);

    var obj = JsonNode.Parse(body);
    accessToken  = obj?["accessToken"]?.GetValue<string>();
    refreshToken = obj?["refreshToken"]?.GetValue<string>();

    if (accessToken == null)
    {
        Console.WriteLine("!! accessToken 없음");
        return;
    }

    http.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    Console.WriteLine($"accessToken  : {accessToken[..Math.Min(40, accessToken.Length)]}...");
    Console.WriteLine($"refreshToken : {refreshToken?[..Math.Min(40, refreshToken?.Length ?? 0)]}...");
}

// ── 2. 토큰 갱신 ──────────────────────────────────────────────────────────────
async Task RunRefreshAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== 2. POST /api/auth/refresh ===");
    var res  = await http.PostAsJsonAsync($"{baseUrl}/api/auth/refresh", new { refreshToken });
    var body = await res.Content.ReadAsStringAsync();
    PrintResponse(res, body);

    var obj = JsonNode.Parse(body);
    var newAccess  = obj?["accessToken"]?.GetValue<string>();
    var newRefresh = obj?["refreshToken"]?.GetValue<string>();
    if (newAccess != null)
    {
        accessToken  = newAccess;
        refreshToken = newRefresh;
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        Console.WriteLine("토큰 갱신 완료");
    }
}

// ── 3. 물류바코드로 Lot 조회 ──────────────────────────────────────────────────
async Task RunLookupAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== 3. POST /api/mantec/lot/lookup_by_barcode ===");
    Console.Write("물류 바코드 (Enter = 12345678901234): ");
    var barcode = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(barcode)) barcode = "12345678901234";

    var res  = await http.PostAsJsonAsync($"{baseUrl}/api/mantec/lot/lookup_by_barcode", new { barcode });
    var body = await res.Content.ReadAsStringAsync();
    PrintResponse(res, body);
}

// ── 4. 검사결과 일괄 저장 ────────────────────────────────────────────────────
async Task RunSaveAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== 4. POST /api/mantec/lot/save_inspection ===");
    Console.Write("barcode: ");
    var barcode = Console.ReadLine() ?? "";
    Console.Write("lot_no (29자리): ");
    var lotNo = Console.ReadLine() ?? "";
    Console.Write("insp_result_code (PASS/FAIL/OK/NG 등): ");
    var code = Console.ReadLine() ?? "PASS";

    var res  = await http.PostAsJsonAsync($"{baseUrl}/api/mantec/lot/save_inspection", new
    {
        inspection_list = new[]
        {
            new { barcode, lot_no = lotNo, insp_result_code = code }
        }
    });
    var body = await res.Content.ReadAsStringAsync();
    PrintResponse(res, body);
}

// ── 5. 생산 Lot 상세 조회 ────────────────────────────────────────────────────
async Task RunDetailAsync()
{
    Console.WriteLine();
    Console.WriteLine("=== 5. POST /api/mantec/lot/detail_with_equipment ===");
    Console.Write("lot_no (29자리): ");
    var lotNo = Console.ReadLine() ?? "";

    var res  = await http.PostAsJsonAsync($"{baseUrl}/api/mantec/lot/detail_with_equipment", new { lot_no = lotNo });
    var body = await res.Content.ReadAsStringAsync();
    PrintResponse(res, body);
}

// ── 공통 출력 ─────────────────────────────────────────────────────────────────
void PrintResponse(HttpResponseMessage res, string body)
{
    Console.WriteLine($"Status : {(int)res.StatusCode} {res.StatusCode}");
    try   { Console.WriteLine(JsonNode.Parse(body)?.ToJsonString(jsonOpt)); }
    catch { Console.WriteLine(body); }
}
