using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FAST.Gate.Client.Abstractions;
using FAST.Gate.Client.Configuration;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Client.Auth;

/// <summary>
/// HTTP client used by FAST applications to communicate with FAST.Gate.
///
/// This is the only class FAST applications need to interact with for all
/// authentication and authorization needs. They never talk to Logto directly.
///
/// Supports:
///   - Direct login (username/password) for Blazor Server
///   - SSO redirect flows for Blazor WASM and web apps
///   - Silent token refresh
///   - Token validation
///   - Logout
///   - User profile fetch
/// </summary>
public sealed class GateAuthClient
{
    private readonly HttpClient _http;
    private readonly GateClientOptions _options;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    public GateAuthClient(HttpClient http, IOptions<GateClientOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    // ── Login (not supported with Logto — use SSO) ──────────────────────────

    /// <summary>
    /// Not supported when using Logto as the identity provider.
    /// Logto requires browser-based authentication via SSO.
    ///
    /// Use <see cref="GetSsoLoginUrlAsync"/> instead:
    ///   1. Call GetSsoLoginUrlAsync() to get the redirect URL
    ///   2. Redirect the user's browser to that URL
    ///   3. Handle the callback and call ExchangeSsoCodeAsync()
    /// </summary>
    [Obsolete("Direct login is not supported with Logto. Use GetSsoLoginUrlAsync() and ExchangeSsoCodeAsync() instead.", error: true)]
    public Task<FastAuthResult> LoginAsync(
        string loginName,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Direct username/password login is not supported with the Logto provider. " +
            "Use GetSsoLoginUrlAsync() to start the SSO flow.");

    // ── SSO ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the SSO redirect URL.
    /// Redirect the user's browser to this URL to initiate SSO login at the IdP.
    /// </summary>
    public async Task<string> GetSsoLoginUrlAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SsoRedirectUri))
            throw new InvalidOperationException(
                "FastGate:SsoRedirectUri must be configured when using SSO. " +
                "Set it in appsettings.json under the FastGate section.");

        var redirectUri = Uri.EscapeDataString(_options.SsoRedirectUri!);
        var tenant = tenantId is not null ? $"&tenantId={Uri.EscapeDataString(tenantId)}" : string.Empty;
        var response = await _http.GetAsync(
            $"/auth/sso/url?redirectUri={redirectUri}{tenant}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SsoUrlResponse>(_json, cancellationToken);
        return result?.Url ?? throw new InvalidOperationException("FAST.Gate returned no SSO URL.");
    }

    /// <summary>
    /// Exchanges the SSO auth code (received at the callback URL) for a full FastAuthResult.
    /// Call this in the SSO callback handler of your Blazor WASM or web app.
    /// </summary>
    public async Task<FastAuthResult> ExchangeSsoCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SsoRedirectUri))
            throw new InvalidOperationException(
                "FastGate:SsoRedirectUri must be configured when using SSO. " +
                "Set it in appsettings.json under the FastGate section.");

        var payload = new { code, redirectUri = _options.SsoRedirectUri! };
        return await PostAsync<FastAuthResult>("/auth/sso/exchange", payload, cancellationToken)
            ?? FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.InternalError, "Empty response from FAST.Gate.", GetRequestId()));
    }

    // ── Token management ──────────────────────────────────────────────────────

    /// <summary>
    /// Silently refreshes the access token using the stored refresh token.
    /// Call this when the access token is near expiry (use TokenRefreshBufferSeconds).
    /// If this fails (refresh token expired), the user must re-login.
    /// </summary>
    public async Task<FastAuthResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var payload = new { refreshToken };
        return await PostAsync<FastAuthResult>("/auth/refresh", payload, cancellationToken)
            ?? FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.InternalError, "Empty response from FAST.Gate.", GetRequestId()));
    }

    /// <summary>
    /// Validates an access token and returns the associated user profile.
    /// Used by FAST services to authenticate inbound requests.
    /// </summary>
    public async Task<FastAuthResult> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs out the user by revoking tokens at the IdP.
    /// </summary>
    public async Task LogoutAsync(
        string accessToken,
        string? refreshToken = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new { accessToken, refreshToken };
        await PostAsync<object>("/auth/logout", payload, cancellationToken);
    }

    // ── User profile ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the current user's profile.
    /// </summary>
    public async Task<UserProfile?> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserProfile>(_json, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T?> PostAsync<T>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsJsonAsync(path, payload, cancellationToken);
        // Always deserialize — FAST.Gate returns FastAuthResult on both success and failure
        return await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken);
    }

    private async Task<FastAuthResult> ReadResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var result = await response.Content
            .ReadFromJsonAsync<FastAuthResult>(_json, cancellationToken);
        return result ?? FastAuthResult.Failure(
            GateError.Create(GateErrorCodes.InternalError,
                "Empty response from FAST.Gate.", GetRequestId()));
    }

    private static string GetRequestId() => Guid.NewGuid().ToString("N");

    private sealed record SsoUrlResponse(string Url);
}
