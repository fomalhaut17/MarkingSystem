using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Services;

public sealed class AppSettings
{
    [JsonPropertyName("Plc")]  public PlcSettings Plc { get; init; } = new();
    [JsonPropertyName("Api")]  public ApiSettings Api { get; init; } = new();

    /// <summary>
    /// 실행 파일 옆의 appsettings.json을 읽어 반환한다.
    /// 파일이 없으면 기본값(TCP Mock) 인스턴스를 반환한다.
    /// </summary>
    public static AppSettings Load()
    {
        var dir  = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, "appsettings.json");

        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}

public sealed class PlcSettings
{
    [JsonPropertyName("Mode")]   public string            Mode   { get; init; } = "Tcp";
    [JsonPropertyName("Tcp")]    public TcpPlcSettings    Tcp    { get; init; } = new();
    [JsonPropertyName("Serial")] public SerialPlcSettings Serial { get; init; } = new();
    [JsonPropertyName("Memory")] public PlcMemorySettings Memory { get; init; } = new();
}

public sealed class TcpPlcSettings
{
    [JsonPropertyName("Host")] public string Host { get; init; } = "127.0.0.1";
    [JsonPropertyName("Port")] public int    Port { get; init; } = 2004;
}

public sealed class SerialPlcSettings
{
    [JsonPropertyName("PortName")]   public string PortName   { get; init; } = "COM3";
    [JsonPropertyName("BaudRate")]   public int    BaudRate   { get; init; } = 9600;
    [JsonPropertyName("StationNo")]  public string StationNo  { get; init; } = "01";
}

/// <summary>
/// PLC 메모리 주소 설정. 실제 PLC 프로그램 확정 전까지 기본값은 임시(TBD).
/// 주소 확정 후 appsettings.json의 Plc.Memory 섹션만 수정하면 된다.
/// </summary>
public sealed class PlcMemorySettings
{
    /// <summary>PC → PLC: Lot 바코드 #1 (낮은 Ser), 15 Word = 30 bytes ASCII [TBD]</summary>
    [JsonPropertyName("LotBarcode1Addr")]     public string LotBarcode1Addr     { get; init; } = "%MW100";
    /// <summary>PC → PLC: Lot 바코드 #2 (높은 Ser), 15 Word = 30 bytes ASCII [TBD]</summary>
    [JsonPropertyName("LotBarcode2Addr")]     public string LotBarcode2Addr     { get; init; } = "%MW120";
    [JsonPropertyName("LotBarcodeWordCount")] public int    LotBarcodeWordCount { get; init; } = 15;
    /// <summary>PLC → PC: 발행 요청 (1 = 요청, 0 = 대기) [TBD]</summary>
    [JsonPropertyName("BarcodeRequestAddr")]  public string BarcodeRequestAddr  { get; init; } = "%MW140";
    /// <summary>Scanner → PLC → PC: 스캐너 읽기 Lot #11 [TBD]</summary>
    [JsonPropertyName("ScannedBarcode1Addr")] public string ScannedBarcode1Addr { get; init; } = "%MW150";
    /// <summary>Scanner → PLC → PC: 스캐너 읽기 Lot #12 [TBD]</summary>
    [JsonPropertyName("ScannedBarcode2Addr")] public string ScannedBarcode2Addr { get; init; } = "%MW170";
}

public sealed class ApiSettings
{
    [JsonPropertyName("BaseUrl")]     public string BaseUrl     { get; init; } = "http://localhost:3000/api/marking";
    [JsonPropertyName("AuthBaseUrl")] public string AuthBaseUrl { get; init; } = "http://localhost:3000/auth";
}
