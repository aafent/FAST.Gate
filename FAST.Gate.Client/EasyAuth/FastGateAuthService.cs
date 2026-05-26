// FastGateAuthService.cs
using System.Net.Http.Json;

namespace FAST.Gate.Client;

internal sealed class FastGateAuthService : IFastGateAuthService
{
    private readonly HttpClient _http;
    private readonly FastGateConfig _config;
    private readonly FastGateSession _session;

    public FastGateAuthService(
        HttpClient http,
        FastGateConfig config,
        FastGateSession session)
    {
        _http = http;
        _config = config;
        _session = session;
    }

    public async Task<string> GetLoginUrlAsync(CancellationToken cancellationToken = default)
    {
        // GET /auth/sso/url?redirectUri=...
        var uri = $"{_config.GateBaseUrl.TrimEnd('/')}/auth/sso/url" +
                  $"?redirectUri={Uri.EscapeDataString(_config.SsoRedirectUri)}";

        using var response = await _http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Shape depends on current FAST.Gate implementation; assume { "url": "..." }
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
        if (payload == null || !payload.TryGetValue("url", out var url))
        {
            throw new InvalidOperationException("FAST.Gate did not return an SSO URL.");
        }

        return url;
    }

    public async Task<FastLoginResult> CompleteLoginAsync(
        string code,
        string? state,
        CancellationToken cancellationToken = default)
    {
        // POST /auth/sso/exchange
        var uri = $"{_config.GateBaseUrl.TrimEnd('/')}/auth/sso/exchange";

        var body = new
        {
            code,
            state,
            redirectUri = _config.SsoRedirectUri
        };

        using var response = await _http.PostAsJsonAsync(uri, body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Map known error codes (gate_invalid_credentials, gate_invalid_token, etc.)[file:1]
            var error = await TryReadErrorAsync(response, cancellationToken);
            return new FastLoginResult
            {
                Status = error?.ErrorCode == "invalid_grant"
                    ? FastLoginStatus.InvalidGrant
                    : FastLoginStatus.Error,
                ErrorCode = error?.ErrorCode,
                ErrorDescription = error?.ErrorDescription
            };
        }

        var authResult = await response.Content.ReadFromJsonAsync<FastAuthWireModel>(cancellationToken)
                         ?? throw new InvalidOperationException("Empty auth result from FAST.Gate.");

        // Map to session
        _session.IsAuthenticated = true;
        _session.AccessToken = authResult.AccessToken;
        _session.RefreshToken = authResult.RefreshToken;
        _session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(authResult.ExpiresIn);

        _session.User = new FastUserInfo
        {
            UserId = authResult.Sub,
            Email = authResult.Email,
            Name = authResult.Name,
            TenantId = authResult.TenantId,
            Roles = authResult.Roles ?? Array.Empty<string>(),
            Abilities = authResult.Abilities ?? Array.Empty<string>()
        };

        return new FastLoginResult { Status = FastLoginStatus.Success };
    }

    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || !_session.ExpiresAt.HasValue)
            return;

        var now = DateTimeOffset.UtcNow;
        var buffer = TimeSpan.FromSeconds(_config.TokenRefreshBufferSeconds);

        // If token is not close to expiry, skip refresh
        if (_session.ExpiresAt.Value - now > buffer)
            return;

        if (string.IsNullOrEmpty(_session.RefreshToken))
            return;

        var uri = $"{_config.GateBaseUrl.TrimEnd('/')}/auth/refresh";

        var body = new { refreshToken = _session.RefreshToken };

        using var response = await _http.PostAsJsonAsync(uri, body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // If refresh fails, let app handle re-login later
            return;
        }

        var authResult = await response.Content.ReadFromJsonAsync<FastAuthWireModel>(cancellationToken);
        if (authResult == null)
            return;

        _session.AccessToken = authResult.AccessToken;
        _session.RefreshToken = authResult.RefreshToken;
        _session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(authResult.ExpiresIn);

        _session.User = new FastUserInfo
        {
            UserId = authResult.Sub,
            Email = authResult.Email,
            Name = authResult.Name,
            TenantId = authResult.TenantId,
            Roles = authResult.Roles ?? Array.Empty<string>(),
            Abilities = authResult.Abilities ?? Array.Empty<string>()
        };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var uri = $"{_config.GateBaseUrl.TrimEnd('/')}/auth/logout";

        if (!string.IsNullOrEmpty(_session.RefreshToken))
        {
            var body = new { refreshToken = _session.RefreshToken };
            await _http.PostAsJsonAsync(uri, body, cancellationToken);
        }

        // Clear local session regardless of HTTP result
        _session.IsAuthenticated = false;
        _session.AccessToken = null;
        _session.RefreshToken = null;
        _session.ExpiresAt = null;
        _session.User = null;
    }

    private static async Task<FastErrorWireModel?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<FastErrorWireModel>(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private sealed class FastErrorWireModel
    {
        public string? ErrorCode { get; set; }
        public string? ErrorDescription { get; set; }
    }

    // This should match what FAST.Gate already returns from /auth/sso/exchange and /auth/refresh.
    private sealed class FastAuthWireModel
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }

        public string? Sub { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? TenantId { get; set; }

        public string[]? Roles { get; set; }
        public string[]? Abilities { get; set; }
    }
}