using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkingSystem.Services;

/// <summary>
/// JWT 인증 서비스.
/// Access Token  : 항상 메모리에만 보관 (앱 종료 시 소멸).
/// Refresh Token : rememberMe=true 시 파일(%APPDATA%\ManntekMarkingSystem\auth.json) 저장,
///                 false 시 메모리에만 보관.
/// </summary>
public sealed class AuthService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _authBaseUrl;
    private readonly string _tokenFilePath;

    private string? _accessToken;
    private string? _refreshToken;
    private bool    _rememberMe;   // 현재 세션의 저장 방식을 기억

    // 로그인 폼 복원용 (rememberMe=true 시 파일에 저장)
    public string? SavedCompanyCode { get; private set; }
    public string? SavedUsername    { get; private set; }

    public event EventHandler? LogoutRequested;

    public AuthService(string authBaseUrl)
    {
        _authBaseUrl   = authBaseUrl.TrimEnd('/');
        _tokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ManntekMarkingSystem", "auth.json");
    }

    public string? AccessToken => _accessToken;

    // ── 자동 로그인 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 파일에 저장된 Refresh Token으로 자동 로그인을 시도한다.
    /// 성공 시 true, 저장된 토큰 없거나 갱신 실패 시 false.
    /// </summary>
    public async Task<bool> TryAutoLoginAsync()
    {
        LoadFromFile();
        if (string.IsNullOrEmpty(_refreshToken)) return false;

        _rememberMe = true;   // 파일에 있었으므로 이번 세션도 파일 저장 유지
        return await RefreshAsync();
    }

    // ── 로그인 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 업체코드/아이디/비밀번호로 로그인한다.
    /// rememberMe=true 시 Refresh Token을 파일에 저장, false 시 메모리만 보관.
    /// 성공 시 null, 실패 시 오류 메시지 반환.
    /// </summary>
    public async Task<string?> LoginAsync(string companyCode, string username, string password, bool rememberMe)
    {
        try
        {
            var body     = new { companyCode, username, password };
            var response = await _http.PostAsJsonAsync($"{_authBaseUrl}/login", body);
            var wrapper  = await response.Content.ReadFromJsonAsync<AuthApiResponse<TokenData>>();

            if (response.IsSuccessStatusCode && wrapper?.Success == true && wrapper.Data != null)
            {
                _accessToken  = wrapper.Data.AccessToken;   // 메모리만
                _refreshToken = wrapper.Data.RefreshToken;
                _rememberMe   = rememberMe;

                if (rememberMe)
                {
                    SavedCompanyCode = companyCode;
                    SavedUsername    = username;
                    SaveToFile();
                }
                else
                {
                    SavedCompanyCode = null;
                    SavedUsername    = null;
                    DeleteTokenFile();
                }

                return null;
            }

            return wrapper?.Error?.Message ?? "로그인에 실패했습니다.";
        }
        catch (Exception ex)
        {
            return $"서버 연결 오류: {ex.Message}";
        }
    }

    // ── 토큰 갱신 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Refresh Token으로 Access Token을 갱신한다.
    /// 성공 시 true. 실패 시 Refresh Token 파일 삭제 후 LogoutRequested 발화하고 false 반환.
    /// </summary>
    public async Task<bool> RefreshAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken)) return false;

        try
        {
            var body     = new { refreshToken = _refreshToken };
            var response = await _http.PostAsJsonAsync($"{_authBaseUrl}/refresh", body);
            var wrapper  = await response.Content.ReadFromJsonAsync<AuthApiResponse<TokenData>>();

            if (response.IsSuccessStatusCode && wrapper?.Success == true && wrapper.Data != null)
            {
                _accessToken  = wrapper.Data.AccessToken;   // 메모리만
                _refreshToken = wrapper.Data.RefreshToken;

                if (_rememberMe) SaveToFile();  // 기존 저장 방식 유지

                return true;
            }
        }
        catch { }

        // 갱신 실패 → 자동 로그아웃
        Logout();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
        return false;
    }

    // ── 로그아웃 ───────────────────────────────────────────────────────────────

    public void Logout()
    {
        _accessToken  = null;
        _refreshToken = null;
        _rememberMe   = false;
        // SavedCompanyCode / SavedUsername 은 유지 — 로그인 폼 복원에 사용
        DeleteTokenFile();
    }

    // ── 파일 I/O ──────────────────────────────────────────────────────────────

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_tokenFilePath)) return;
            var json = File.ReadAllText(_tokenFilePath);
            var data = JsonSerializer.Deserialize<StoredToken>(json);
            _refreshToken    = data?.RefreshToken;
            SavedCompanyCode = data?.CompanyCode;
            SavedUsername    = data?.Username;
        }
        catch { }
    }

    private void SaveToFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_tokenFilePath)!);
            var json = JsonSerializer.Serialize(new StoredToken(_refreshToken, SavedCompanyCode, SavedUsername));
            File.WriteAllText(_tokenFilePath, json);
        }
        catch { }
    }

    private void DeleteTokenFile()
    {
        try { if (File.Exists(_tokenFilePath)) File.Delete(_tokenFilePath); }
        catch { }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed record StoredToken(
        [property: JsonPropertyName("refreshToken")] string? RefreshToken,
        [property: JsonPropertyName("companyCode")]  string? CompanyCode,
        [property: JsonPropertyName("username")]     string? Username);

    private sealed class TokenData
    {
        [JsonPropertyName("accessToken")]  public string? AccessToken  { get; init; }
        [JsonPropertyName("refreshToken")] public string? RefreshToken { get; init; }
        [JsonPropertyName("expiresIn")]    public int     ExpiresIn    { get; init; }
    }

    private sealed class AuthApiResponse<T>
    {
        [JsonPropertyName("success")] public bool      Success { get; init; }
        [JsonPropertyName("data")]    public T?         Data    { get; init; }
        [JsonPropertyName("error")]   public AuthError? Error   { get; init; }
    }

    private sealed class AuthError
    {
        [JsonPropertyName("code")]    public string? Code    { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
    }
}
