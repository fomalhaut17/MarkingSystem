using MarkingSystem.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Services;

/// <summary>
/// wizMES REST API 클라이언트.
/// 모든 요청에 AuthService에서 발급받은 Bearer 토큰을 자동 첨부한다.
/// 401 응답 시 토큰 갱신 후 1회 재시도한다.
/// </summary>
public sealed class WizMesApiClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string      _baseUrl;
    private readonly AuthService _auth;

    public WizMesApiClient(string baseUrl, AuthService auth)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _auth    = auth;
    }

    // ── API 1: 자재 정보 조회 ─────────────────────────────────────────────────

    public async Task<MaterialInfo?> GetMaterialAsync(string materialBarcode)
    {
        var url     = $"{_baseUrl}/materials/{Uri.EscapeDataString(materialBarcode)}";
        var wrapper = await SendWithRetryAsync<MaterialInfo>(
            () => BuildGet(url));
        return wrapper?.Success == true ? wrapper.Data : null;
    }

    // ── API 2: 발행 결과 일괄 전송 ───────────────────────────────────────────

    public async Task<int> PostIssueResultsAsync(string materialBarcode, IEnumerable<IssueResultDto> results)
    {
        var body    = new { materialBarcode, results };
        var wrapper = await SendWithRetryAsync<IssueResultResponse>(
            () => BuildPost($"{_baseUrl}/issue-results", body));
        return wrapper?.Success == true ? wrapper.Data?.SavedCount ?? 0 : 0;
    }

    // ── API 3: Lot 목록 조회 ─────────────────────────────────────────────────

    public async Task<List<LotItem>> GetLotsAsync(string? materialBarcode, string? lotCode)
    {
        var query   = materialBarcode != null
            ? $"materialBarcode={Uri.EscapeDataString(materialBarcode)}"
            : $"lotCode={Uri.EscapeDataString(lotCode!)}";
        var wrapper = await SendWithRetryAsync<LotListResponse>(
            () => BuildGet($"{_baseUrl}/lots?{query}"));
        return wrapper?.Success == true ? wrapper.Data?.Items ?? [] : [];
    }

    // ── API 4: 생산 Lot 상세 조회 ─────────────────────────────────────────────

    public async Task<ProductionLotInfo?> GetProductionLotAsync(string lotCode)
    {
        var url     = $"{_baseUrl}/production-lots/{Uri.EscapeDataString(lotCode)}";
        var wrapper = await SendWithRetryAsync<ProductionLotInfo>(
            () => BuildGet(url));
        return wrapper?.Success == true ? wrapper.Data : null;
    }

    // ── TEST: 초기화 ──────────────────────────────────────────────────────────

    public async Task ResetTestDataAsync(string materialBarcode)
    {
        try
        {
            var url = $"{_baseUrl}/test/reset?materialBarcode={Uri.EscapeDataString(materialBarcode)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            AttachToken(req);
            await _http.SendAsync(req);
        }
        catch { /* 실패해도 무시 */ }
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildGet(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AttachToken(req);
        return req;
    }

    private HttpRequestMessage BuildPost<T>(string url, T body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        AttachToken(req);
        return req;
    }

    private void AttachToken(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_auth.AccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AccessToken);
    }

    /// <summary>
    /// 요청 실행 → 401이면 토큰 갱신 후 1회 재시도.
    /// </summary>
    private async Task<ApiResponse<T>?> SendWithRetryAsync<T>(Func<HttpRequestMessage> buildRequest)
    {
        var response = await _http.SendAsync(buildRequest());

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshed = await _auth.RefreshAsync();
            if (!refreshed) return null;

            response = await _http.SendAsync(buildRequest());
        }

        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

public sealed record IssueResultDto(
    [property: JsonPropertyName("lotBarcode")]       string LotBarcode,
    [property: JsonPropertyName("inspectionResult")] string InspectionResult);

sealed class LotListResponse
{
    [JsonPropertyName("items")]      public List<LotItem>? Items      { get; init; }
    [JsonPropertyName("totalCount")] public int            TotalCount { get; init; }
}

sealed class IssueResultResponse
{
    [JsonPropertyName("savedCount")]
    public int SavedCount { get; init; }
}

// ── 공통 응답 래퍼 ────────────────────────────────────────────────────────────

sealed class ApiResponse<T>
{
    [JsonPropertyName("success")] public bool     Success { get; init; }
    [JsonPropertyName("data")]    public T?       Data    { get; init; }
    [JsonPropertyName("error")]   public ApiError? Error  { get; init; }
}

sealed class ApiError
{
    [JsonPropertyName("code")]    public string? Code    { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}
