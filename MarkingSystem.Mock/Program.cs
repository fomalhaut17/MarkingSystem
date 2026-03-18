using System.Text.Json;
using MarkingSystem.Mock;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "[MOCK] MarkingSystem.Mock";

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine();
Console.WriteLine("  ┌──────────────────────────────────────────────────┐");
Console.WriteLine("  │  MarkingSystem.Mock  —  NOT a real server        │");
Console.WriteLine("  │  For testing purposes only. Not for production.  │");
Console.WriteLine("  └──────────────────────────────────────────────────┘");
Console.ResetColor();
Console.WriteLine();

// ── appsettings.json 로드 ────────────────────────────────────────────────────
// 탐색 순서: 1) exe 위치 (publish/release), 2) cwd (dotnet run 개발)
static string? FindFile(string name)
{
    var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
    if (exeDir is not null)
    {
        var p = Path.Combine(exeDir, name);
        if (File.Exists(p)) return p;
    }
    var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), name);
    return File.Exists(cwdPath) ? cwdPath : null;
}

var mode = "local";
var modeSelectorPath = FindFile("appsettings.json");
if (modeSelectorPath is not null)
{
    try
    {
        var doc = JsonDocument.Parse(File.ReadAllText(modeSelectorPath));
        if (doc.RootElement.TryGetProperty("AppMode", out var appModeEl))
            mode = appModeEl.GetString()?.ToLowerInvariant() ?? "local";
    }
    catch { }
}

var plcMode    = "Tcp";
var tcpPort    = 2004;
var serialPort = "COM6";   // com0com 기본 mock 포트 (앱=COM5, mock=COM6)
var baudRate   = 9600;
var stationNo  = "01";

var modePath = FindFile($"appsettings.{mode}.json");
if (modePath is not null)
{
    try
    {
        var doc = JsonDocument.Parse(File.ReadAllText(modePath));

        // Plc 섹션: mode, TCP port, serial baud/station
        if (doc.RootElement.TryGetProperty("Plc", out var plc))
        {
            if (plc.TryGetProperty("Mode", out var m))
                plcMode = m.GetString() ?? "Tcp";
            if (plc.TryGetProperty("Tcp", out var tcp))
                if (tcp.TryGetProperty("Port", out var p)) tcpPort = p.GetInt32();
            if (plc.TryGetProperty("Serial", out var serial))
            {
                if (serial.TryGetProperty("BaudRate",  out var br)) baudRate  = br.GetInt32();
                if (serial.TryGetProperty("StationNo", out var sn)) stationNo = sn.GetString() ?? stationNo;
            }
        }

        // Mock.Serial.PortName: mock이 열 COM 포트 (앱과 다른 쪽 가상 포트)
        if (doc.RootElement.TryGetProperty("Mock", out var mock) &&
            mock.TryGetProperty("Serial", out var ms) &&
            ms.TryGetProperty("PortName", out var pn))
        {
            serialPort = pn.GetString() ?? serialPort;
        }
    }
    catch { }
}

Console.WriteLine($"[MOCK] AppMode={mode}, Plc.Mode={plcMode}");

// ─────────────────────────────────────────────────────────────────────────────

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var apiServer = new MockApiServer(port: 3000);

try
{
    if (string.Equals(plcMode, "Serial", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"[MOCK] Serial PLC mock: {serialPort} @ {baudRate}bps, station={stationNo}");
        var serialServer = new MockCnetSerialServer(serialPort, baudRate, stationNo);
        await Task.WhenAll(
            apiServer.RunAsync(cts.Token),
            serialServer.RunAsync(cts.Token)
        );
    }
    else
    {
        Console.WriteLine($"[MOCK] TCP PLC mock: port={tcpPort}");
        var plcServer = new MockPlcServer(port: tcpPort);
        await Task.WhenAll(
            apiServer.RunAsync(cts.Token),
            plcServer.RunAsync(cts.Token)
        );
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("[MOCK] Servers stopped.");
