using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Services;

/// <summary>appsettings.json에서 AppMode만 읽기 위한 내부 클래스.</summary>
internal sealed class AppModeSelector
{
    [JsonPropertyName("AppMode")] public string? AppMode { get; init; }
}

public sealed class AppSettings
{
    [JsonIgnore]                        public string          AppMode  { get; set; } = "local";
    [JsonPropertyName("Plc")]           public PlcSettings     Plc      { get; init; } = new();
    [JsonPropertyName("Api")]           public ApiSettings     Api      { get; init; } = new();
    [JsonPropertyName("Marking")]       public MarkingSettings Marking  { get; init; } = new();

    /// <summary>
    /// 실행 파일 옆의 appsettings.json에서 AppMode를 읽고,
    /// appsettings.{mode}.json을 로드하여 반환한다.
    /// 파일이 없으면 기본값(local / TCP Mock) 인스턴스를 반환한다.
    /// </summary>
    public static AppSettings Load()
    {
        var dir     = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 1) appsettings.json → AppMode 읽기
        var modeSelectorPath = Path.Combine(dir, "appsettings.json");
        var mode = "local";
        if (File.Exists(modeSelectorPath))
        {
            try
            {
                var selector = JsonSerializer.Deserialize<AppModeSelector>(
                    File.ReadAllText(modeSelectorPath), options);
                if (!string.IsNullOrWhiteSpace(selector?.AppMode))
                    mode = selector.AppMode.ToLowerInvariant();
            }
            catch { }
        }

        // 2) appsettings.{mode}.json → 실제 설정 로드
        var modePath = Path.Combine(dir, $"appsettings.{mode}.json");
        if (!File.Exists(modePath)) return new AppSettings { AppMode = mode };

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                               File.ReadAllText(modePath), options)
                           ?? new AppSettings();
            settings.AppMode = mode;
            return settings;
        }
        catch
        {
            return new AppSettings { AppMode = mode };
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

public sealed class MarkingSettings
{
    /// <summary>발행 종료 시 진행 중인 전송 완료 대기 타임아웃 (ms)</summary>
    [JsonPropertyName("StopWaitTimeoutMs")] public int StopWaitTimeoutMs { get; init; } = 10000;
}

public sealed class ApiSettings
{
    [JsonPropertyName("BaseUrl")]     public string BaseUrl     { get; init; } = "http://localhost:3000/api/marking";
    [JsonPropertyName("AuthBaseUrl")] public string AuthBaseUrl { get; init; } = "http://localhost:3000/auth";
}
