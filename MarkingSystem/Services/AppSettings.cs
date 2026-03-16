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
    [JsonPropertyName("Mode")]   public string         Mode   { get; init; } = "Tcp";
    [JsonPropertyName("Tcp")]    public TcpPlcSettings    Tcp    { get; init; } = new();
    [JsonPropertyName("Serial")] public SerialPlcSettings Serial { get; init; } = new();
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

public sealed class ApiSettings
{
    [JsonPropertyName("BaseUrl")]     public string BaseUrl     { get; init; } = "http://localhost:3000/api/marking";
    [JsonPropertyName("AuthBaseUrl")] public string AuthBaseUrl { get; init; } = "http://localhost:3000/auth";
}
