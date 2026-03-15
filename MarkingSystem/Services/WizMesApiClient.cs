using MarkingSystem.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Services;

/// <summary>
/// wizMES REST API 클라이언트.
/// Base URL: appsettings 또는 App.config에서 주입 (현재는 생성자 인자).
/// </summary>
public sealed class WizMesApiClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _baseUrl;

    public WizMesApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    // ── API 1: 자재 정보 조회 ─────────────────────────────────────────────────

    /// <summary>
    /// 물류 바코드로 자재 정보를 조회한다. (§6.3.1)
    /// 실패 시 null 반환.
    /// </summary>
    public async Task<MaterialInfo?> GetMaterialAsync(string materialBarcode)
    {
        var url = $"{_baseUrl}/materials/{Uri.EscapeDataString(materialBarcode)}";
        var wrapper = await _http.GetFromJsonAsync<ApiResponse<MaterialInfo>>(url);
        return wrapper?.Success == true ? wrapper.Data : null;
    }

    // ── API 2: 발행 결과 일괄 전송 ───────────────────────────────────────────

    /// <summary>
    /// 검사 결과 목록을 wizMES에 전송한다. (§6.3.2)
    /// 성공 시 저장된 건수 반환, 실패 시 0 반환.
    /// </summary>
    public async Task<int> PostIssueResultsAsync(string materialBarcode, IEnumerable<IssueResultDto> results)
    {
        var body    = new { materialBarcode, results };
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/issue-results", body);
        if (!response.IsSuccessStatusCode) return 0;

        var wrapper = await response.Content.ReadFromJsonAsync<ApiResponse<IssueResultResponse>>();
        return wrapper?.Success == true ? wrapper.Data?.SavedCount ?? 0 : 0;
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

public sealed record IssueResultDto(
    [property: JsonPropertyName("lotBarcode")]       string LotBarcode,
    [property: JsonPropertyName("inspectionResult")] string InspectionResult);

file sealed class IssueResultResponse
{
    [JsonPropertyName("savedCount")]
    public int SavedCount { get; init; }
}

// ── 공통 응답 래퍼 ────────────────────────────────────────────────────────────

file sealed class ApiResponse<T>
{
    [JsonPropertyName("success")] public bool     Success { get; init; }
    [JsonPropertyName("data")]    public T?       Data    { get; init; }
    [JsonPropertyName("error")]   public ApiError? Error  { get; init; }
}

file sealed class ApiError
{
    [JsonPropertyName("code")]    public string? Code    { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
