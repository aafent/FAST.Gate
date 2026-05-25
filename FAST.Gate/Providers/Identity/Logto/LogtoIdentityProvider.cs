using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FAST.Gate.Client.Abstractions;
using FAST.Gate.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FAST.Gate.Providers.Identity.Logto;

/// <summary>
/// Logto implementation of <see cref="IIdentityProvider"/>.
/// Handles all authentication and authorization for the FAST ecosystem via Logto Cloud.
/// FAST applications never interact with this class directly.
/// </summary>
public sealed class LogtoIdentityProvider : IIdentityProvider
{
    private readonly LogtoOptions _opts;
    private readonly CacheOptions _cacheOpts;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LogtoIdentityProvider> _logger;

    private ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private OpenIdConnectConfiguration? _oidcConfig;
    private readonly JsonWebTokenHandler _jwtHandler = new();
    private readonly ConcurrentDictionary<string, TokenCacheEntry> _tokenCache = new();

    // Profile cache: keyed by user id (sub), populated on SSO exchange and refresh
    private readonly ConcurrentDictionary<string, UserProfile> _profileCache = new();

    // Service token cache
    private string? _serviceToken;
    private DateTimeOffset _serviceTokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _serviceTokenLock = new(1, 1);

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    public LogtoIdentityProvider(
        IOptions<GateOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LogtoIdentityProvider> logger)
    {
        _opts = options.Value.Identity.Logto;
        _cacheOpts = options.Value.Cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ServiceClientId))
            throw new InvalidOperationException(
                "Gate:Identity:Logto:ServiceClientId is required. " +
                "Create a Machine-to-Machine app in Logto and paste the Client ID.");

        if (string.IsNullOrWhiteSpace(_opts.ServiceClientSecret))
            throw new InvalidOperationException(
                "Gate:Identity:Logto:ServiceClientSecret is required. " +
                "Set it via environment variable or appsettings — never hardcode it.");

        if (string.IsNullOrWhiteSpace(_opts.WebClientId))
            throw new InvalidOperationException(
                "Gate:Identity:Logto:WebClientId is required. " +
                "Create a Traditional Web application in Logto for the SSO flow and paste its App ID.");

        if (string.IsNullOrWhiteSpace(_opts.WebClientSecret))
            throw new InvalidOperationException(
                "Gate:Identity:Logto:WebClientSecret is required. " +
                "Set it via environment variable or appsettings — never hardcode it.");

        _logger.LogInformation("Initializing Logto provider. Issuer={Issuer}", _opts.IssuerUrl);

        var metadataAddress = $"{_opts.IssuerUrl.TrimEnd('/')}/.well-known/openid-configuration";
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = _opts.IssuerUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) })
        {
            AutomaticRefreshInterval = TimeSpan.FromMilliseconds(_opts.JwksCacheTtlMs),
        };

        _oidcConfig = await _configManager.GetConfigurationAsync(cancellationToken);
        _logger.LogInformation("Logto JWKS loaded. TokenEndpoint={Endpoint}", _oidcConfig.TokenEndpoint);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _tokenCache.Clear();
        _profileCache.Clear();
        _logger.LogInformation("Logto provider shut down.");
        return Task.CompletedTask;
    }

    // ── Authentication ────────────────────────────────────────────────────────

    public Task<FastAuthResult> LoginAsync(
        string loginName,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Logto does not support direct username/password login from a backend service.
        // The Experience API requires a stateful browser session and cannot be called server-to-server.
        //
        // Use the SSO flow instead:
        //   1. Call GetSsoLoginUrl() to get the Logto sign-in URL
        //   2. Redirect the user's browser to that URL
        //   3. Logto redirects back to your callback with an auth code
        //   4. Call ExchangeSsoCodeAsync() to get the FastAuthResult
        //
        // In FAST applications: inject GateAuthClient and call GetSsoLoginUrlAsync().
        throw new NotSupportedException(
            "Direct username/password login is not supported with the Logto provider. " +
            "Use the SSO flow: GetSsoLoginUrl() → user authenticates at Logto → ExchangeSsoCodeAsync().");
    }


    public async Task<FastAuthResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("LogtoHttp");

            // Step 1: Exchange refresh token WITHOUT resource to get a new OIDC token + new refresh token.
            // The refresh token was issued by the Traditional Web app (WebClientId), so we must use
            // WebClientId/WebClientSecret here — using M2M credentials would result in invalid_client.
            var oidcPayload = new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["client_id"]     = _opts.WebClientId,
                ["client_secret"] = _opts.WebClientSecret,
                ["refresh_token"] = refreshToken,
            };

            var oidcResponse = await client.PostAsync(
                _oidcConfig!.TokenEndpoint,
                new FormUrlEncodedContent(oidcPayload),
                cancellationToken);

            if (!oidcResponse.IsSuccessStatusCode)
                return FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.RefreshTokenExpired,
                    "Refresh token expired. Please log in again.",
                    NewRequestId()));

            var oidcJson = await oidcResponse.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            // Extract the new refresh token (Logto rotates refresh tokens)
            var newRefreshToken = oidcJson.TryGetProperty("refresh_token", out var newRt)
                ? newRt.GetString() ?? string.Empty : string.Empty;

            // Step 2: Exchange the new refresh token WITH resource to get an API-audience access token.
            // This is the token FAST apps use to call FAST.Gate endpoints.
            if (!string.IsNullOrWhiteSpace(newRefreshToken) && !string.IsNullOrWhiteSpace(_opts.Audience))
            {
                var apiPayload = new Dictionary<string, string>
                {
                    ["grant_type"]    = "refresh_token",
                    ["client_id"]     = _opts.WebClientId,
                    ["client_secret"] = _opts.WebClientSecret,
                    ["refresh_token"] = newRefreshToken,
                    ["resource"]      = _opts.Audience,
                };

                var apiResponse = await client.PostAsync(
                    _oidcConfig!.TokenEndpoint,
                    new FormUrlEncodedContent(apiPayload),
                    cancellationToken);

                if (apiResponse.IsSuccessStatusCode)
                {
                    var result = await BuildAuthResultFromTokenResponseAsync(
                        apiResponse, newRefreshToken, cancellationToken);
                    return result with { TokenRefreshed = true };
                }

                // If the resource-scoped request fails, fall back to the OIDC token.
                // This keeps the session alive even if the API resource is temporarily unavailable.
                _logger.LogWarning("Resource-scoped refresh failed; falling back to OIDC token.");
            }

            // Fall back: build result from the already-parsed OIDC JSON (response body already consumed above)
            var fallbackResult = await BuildAuthResultFromTokenJsonAsync(
                oidcJson, newRefreshToken, cancellationToken);
            return fallbackResult with { TokenRefreshed = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token refresh.");
            return FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.ProviderError, "Identity provider error.", NewRequestId()));
        }
    }

    public async Task<FastAuthResult> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.MissingToken, "Access token is missing.", NewRequestId()));

        // Cache check
        if (_cacheOpts.TokenCacheEnabled)
        {
            var cacheKey = HashToken(accessToken);
            if (_tokenCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow < cached.EvictAt)
            {
                _logger.LogDebug("Token cache hit. User={User}", cached.Result.User?.LoginName);
                return cached.Result;
            }
            _tokenCache.TryRemove(cacheKey, out _);
        }

        try
        {
            var config = await _configManager!.GetConfigurationAsync(cancellationToken);
            var validationParams = new TokenValidationParameters
            {
                ValidIssuer       = _opts.IssuerUrl,
                ValidAudiences    = new[] { _opts.Audience, _opts.WebClientId },
                IssuerSigningKeys = config.SigningKeys,
                ValidateLifetime  = true,
                ClockSkew         = TimeSpan.FromSeconds(30),
            };

            var result = await _jwtHandler.ValidateTokenAsync(accessToken, validationParams);

            if (!result.IsValid)
            {
                var (code, msg) = ClassifyException(result.Exception);
                return FastAuthResult.Failure(GateError.Create(code, msg, NewRequestId()));
            }

            // Extract user profile from the validated JWT claims.
            // We cannot call the userinfo endpoint here because the access token is an API resource
            // token (aud = https://fast.gate.internal, scope = "") and Logto's userinfo endpoint
            // requires an OIDC token with openid scope. Instead we read claims directly from the
            // validated JWT — sub, roles, and basic profile fields are embedded by Logto.
            // Extract sub from validated token claims
            var sub = result.ClaimsIdentity.Claims
                .FirstOrDefault(c => c.Type == "sub")?.Value ?? string.Empty;

            // Look up cached profile (populated during SSO exchange or refresh).
            // The API resource token does not carry OIDC claims (name, email, roles)
            // so the userinfo endpoint cannot be called with it — we rely on the cache.
            UserProfile? profile = null;
            if (!string.IsNullOrEmpty(sub))
                _profileCache.TryGetValue(sub, out profile);

            // Fallback: if not in cache (e.g. server restarted), build a minimal profile from claims.
            // Roles will be empty — the client should re-authenticate to repopulate the cache.
            if (profile is null)
            {
                var allClaims = result.ClaimsIdentity.Claims.ToList();
                var claims = allClaims
                    .GroupBy(c => c.Type)
                    .ToDictionary(g => g.Key, g => g.First().Value);
                var roleValues = allClaims
                    .Where(c => c.Type == "roles" || c.Type == "role" ||
                                c.Type == System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();
                profile = ExtractProfileFromClaims(claims, roleValues);
                _logger.LogWarning(
                    "Profile cache miss for sub={Sub}. Profile has no name/email/roles. " +
                    "This happens after a server restart — client should re-authenticate.", sub);
            }

            if (profile is null)
                return FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.ProviderError, "Could not retrieve user profile.", NewRequestId()));

            var expiresAt = new DateTimeOffset(result.SecurityToken.ValidTo, TimeSpan.Zero);
            var authResult = FastAuthResult.Success(profile, accessToken, string.Empty, expiresAt);

            // Cache it
            if (_cacheOpts.TokenCacheEnabled)
            {
                EnforceMaxCacheSize();
                var buffer = TimeSpan.FromMilliseconds(_cacheOpts.TokenCacheEarlyExpiryBufferMs);
                var cacheKey = HashToken(accessToken);
                _tokenCache[cacheKey] = new TokenCacheEntry(authResult, expiresAt - buffer);
            }

            return authResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation.");
            return FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.InternalError, "Token validation error.", NewRequestId()));
        }
    }

    public async Task<bool> LogoutAsync(
        string accessToken,
        string? refreshToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Evict from cache
            if (_cacheOpts.TokenCacheEnabled)
                _tokenCache.TryRemove(HashToken(accessToken), out _);

            // Revoke refresh token at Logto if provided
            if (!string.IsNullOrWhiteSpace(refreshToken) &&
                _oidcConfig is not null &&
                _oidcConfig.AdditionalData.TryGetValue("revocation_endpoint", out var revEndpoint) &&
                !string.IsNullOrWhiteSpace(revEndpoint?.ToString()))
            {
                var client = _httpClientFactory.CreateClient("LogtoHttp");
                var payload = new Dictionary<string, string>
                {
                    ["token"]           = refreshToken,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"]       = _opts.ServiceClientId,
                    ["client_secret"]   = _opts.ServiceClientSecret,
                };
                await client.PostAsync(
                    revEndpoint!.ToString()!,
                    new FormUrlEncodedContent(payload),
                    cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout.");
            return false;
        }
    }

    // ── User profile ──────────────────────────────────────────────────────────

    public async Task<UserProfile?> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Decode the sub claim from the JWT without full validation —
            // the token was already validated by the caller or came directly from Logto.
            // Use the profile cache first: API resource tokens have scope="" and the
            // userinfo endpoint will reject them with 401.
            var sub = TryExtractSub(accessToken);
            if (!string.IsNullOrEmpty(sub) && _profileCache.TryGetValue(sub, out var cached))
                return cached;

            // Attempt userinfo endpoint (works when token has openid scope)
            var client  = _httpClientFactory.CreateClient("LogtoHttp");
            var request = new HttpRequestMessage(HttpMethod.Get, _oidcConfig!.UserInfoEndpoint);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Userinfo endpoint returned {Status} for sub={Sub}. " +
                    "Token may be an API resource token (scope=empty). " +
                    "Profile cache miss — client should re-authenticate.",
                    (int)response.StatusCode, sub);
                return null;
            }

            var userInfo = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            var profile = MapToUserProfile(userInfo);

            // Populate cache for future calls
            if (!string.IsNullOrEmpty(profile.Id))
                _profileCache[profile.Id] = profile;

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch user profile from Logto.");
            return null;
        }
    }

    /// <summary>
    /// Decodes the sub claim from a JWT payload without signature verification.
    /// Returns null if the token is opaque or malformed.
    /// </summary>
    private static string? TryExtractSub(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "=";  break;
            }
            var json = JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
            return json.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch { return null; }
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    public bool HasRole(UserProfile user, string role) =>
        user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasAbility(UserProfile user, string ability) =>
        user.Abilities.Contains(ability, StringComparer.OrdinalIgnoreCase);

    // ── SSO ───────────────────────────────────────────────────────────────────

    public string GetSsoLoginUrl(string redirectUri, string state, string? tenantId = null)
    {
        var authEndpoint = _oidcConfig!.AuthorizationEndpoint;
        var clientId = Uri.EscapeDataString(_opts.WebClientId);
        var redirect = Uri.EscapeDataString(redirectUri);
        var scope = Uri.EscapeDataString("openid profile email offline_access roles read write admin");
        var encodedState = Uri.EscapeDataString(state);
        var resource = Uri.EscapeDataString(_opts.Audience);

        var url = $"{authEndpoint}?response_type=code&client_id={clientId}" +
                  $"&redirect_uri={redirect}&scope={scope}&state={encodedState}" +
                  $"&resource={resource}";

        if (!string.IsNullOrWhiteSpace(tenantId))
            url += $"&organization_id={Uri.EscapeDataString(tenantId)}";

        return url;
    }

    public async Task<FastAuthResult> ExchangeSsoCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("LogtoHttp");

            // Step 1: Exchange auth code WITHOUT resource.
            // Logto only returns a refresh token when no resource is requested.
            // With resource=<api-identifier>, Logto issues an API-audience token that
            // strips OIDC scopes (scope becomes ""), which prevents refresh token issuance.
            var oidcPayload = new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
                ["client_id"]     = _opts.WebClientId,
                ["client_secret"] = _opts.WebClientSecret,
            };

            var oidcResponse = await client.PostAsync(
                _oidcConfig!.TokenEndpoint,
                new FormUrlEncodedContent(oidcPayload),
                cancellationToken);

            // Read body once — HttpContent stream cannot be re-read
            var _rawBody = await oidcResponse.Content.ReadAsStringAsync();

            if (!oidcResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("SSO code exchange failed: {Status} {Body}",
                    (int)oidcResponse.StatusCode, _rawBody);
                return FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.InvalidToken, "SSO code exchange failed.", NewRequestId()));
            }

            var oidcJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(_rawBody);

            // Capture the refresh token from the OIDC response — this is what we came for
            var refreshToken = oidcJson.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrEmpty(refreshToken))
                _logger.LogWarning(
                    "Logto did not return a refresh token. " +
                    "Ensure 'offline_access' is enabled on the Traditional Web app in the Logto console " +
                    "(App Details → Permissions → enable offline_access). " +
                    "Session refresh will not be available until this is resolved.");

            // Step 2: Exchange the refresh token WITH resource to get an API-audience access token.
            // This token (aud = _opts.Audience) is what FAST apps send to FAST.Gate endpoints.
            if (!string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(_opts.Audience))
            {
                var apiPayload = new Dictionary<string, string>
                {
                    ["grant_type"]    = "refresh_token",
                    ["client_id"]     = _opts.WebClientId,
                    ["client_secret"] = _opts.WebClientSecret,
                    ["refresh_token"] = refreshToken,
                    ["resource"]      = _opts.Audience,
                };

                var apiResponse = await client.PostAsync(
                    _oidcConfig!.TokenEndpoint,
                    new FormUrlEncodedContent(apiPayload),
                    cancellationToken);

                if (apiResponse.IsSuccessStatusCode)
                {
                    // Read body once; use the API-scoped access token but keep the refresh
                    // token from Step 1. The resource-scoped exchange may also return a
                    // rotated refresh token — prefer it if present.
                    var apiJson = await apiResponse.Content
                        .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

                    var rotatedRefreshToken = apiJson.TryGetProperty("refresh_token", out var rotRt)
                        ? rotRt.GetString() ?? refreshToken : refreshToken;

                    return await BuildAuthResultFromTokenJsonAsync(apiJson, rotatedRefreshToken, cancellationToken);
                }

                _logger.LogWarning(
                    "Resource-scoped token exchange failed after OIDC exchange. " +
                    "Returning OIDC token (aud=WebClientId). " +
                    "Check that the Traditional Web app has the FAST.Gate API resource assigned in Logto console.");
            }

            // Fallback: return the OIDC token with whatever refresh token we have.
            // ValidateTokenAsync accepts WebClientId as a valid audience.
            return await BuildAuthResultFromTokenJsonAsync(oidcJson, refreshToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SSO code exchange.");
            return FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.ProviderError, "Identity provider error.", NewRequestId()));
        }
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _configManager!.GetConfigurationAsync(cancellationToken);
            sw.Stop();
            return HealthStatus.Healthy("logto-identity-provider", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return HealthStatus.Unhealthy("logto-identity-provider", ex.Message);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <param name="refreshTokenOverride">
    /// When provided, this value is used as the refresh token in the result instead of
    /// whatever the token response body contains. Use this when the refresh token was
    /// obtained from a prior OIDC exchange and the current response is a resource-scoped
    /// access token (which Logto issues without a refresh token when resource is specified).
    /// Pass <see langword="null"/> to read the refresh token from the response body.
    /// </param>
    private async Task<FastAuthResult> BuildAuthResultFromTokenResponseAsync(
        System.Net.Http.HttpResponseMessage response,
        string? refreshTokenOverride,
        CancellationToken cancellationToken)
    {
        var json = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return await BuildAuthResultFromTokenJsonAsync(json, refreshTokenOverride, cancellationToken);
    }

    private async Task<FastAuthResult> BuildAuthResultFromTokenJsonAsync(
        JsonElement json,
        string? refreshTokenOverride,
        CancellationToken cancellationToken)
    {

        if (!json.TryGetProperty("access_token", out var atEl))
            throw new InvalidOperationException(
                "Logto token response did not contain access_token. Check client credentials and audience.");
        var accessToken  = atEl.GetString()!;
        var refreshToken = refreshTokenOverride
            ?? (json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty);
        var expiresIn    = json.TryGetProperty("expires_in", out var ei)
            ? ei.GetInt32() : 900;
        var expiresAt    = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        // Extract user profile from id_token claims (avoids calling userinfo endpoint
        // which requires an OIDC-only token, not a resource-audience token)
        UserProfile? profile = null;
        if (json.TryGetProperty("id_token", out var idTokenEl) &&
            idTokenEl.GetString() is string idToken)
        {
            profile = ExtractProfileFromIdToken(idToken);
        }

        // Fall back to userinfo endpoint if no id_token
        if (profile is null)
            profile = await GetUserProfileAsync(accessToken, cancellationToken);

        if (profile is null)
            return FastAuthResult.Failure(GateError.Create(
                GateErrorCodes.ProviderError, "Could not retrieve user profile.", NewRequestId()));

        // Cache profile by sub so ValidateTokenAsync can look it up without calling userinfo
        if (!string.IsNullOrEmpty(profile.Id))
            _profileCache[profile.Id] = profile;

        return FastAuthResult.Success(profile, accessToken, refreshToken, expiresAt);
    }

    /// <summary>
    /// Extracts user profile from validated JWT access token claims.
    /// Used by ValidateTokenAsync where the access token is an API resource token
    /// and the userinfo endpoint cannot be called (it requires an OIDC-scoped token).
    /// Returns null if the sub claim is missing.
    /// </summary>
    private static UserProfile? ExtractProfileFromClaims(
        Dictionary<string, string> claims,
        List<string> roles)
    {
        if (!claims.TryGetValue("sub", out var sub) || string.IsNullOrEmpty(sub))
            return null;

        // name claim may be "name" or split into given_name / family_name
        claims.TryGetValue("name", out var fullName);
        claims.TryGetValue("given_name", out var firstName);
        claims.TryGetValue("family_name", out var lastName);

        if (string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(fullName))
        {
            var parts = fullName.Split(' ', 2);
            firstName = parts[0];
            lastName  = parts.Length > 1 ? parts[1] : string.Empty;
        }

        claims.TryGetValue("username", out var username);
        claims.TryGetValue("email", out var email);
        claims.TryGetValue("picture", out var picture);
        claims.TryGetValue("organization_id", out var orgId);

        return new UserProfile
        {
            Id          = sub,
            LoginName   = username ?? string.Empty,
            FirstName   = firstName ?? string.Empty,
            LastName    = lastName  ?? string.Empty,
            Email       = email     ?? string.Empty,
            AvatarUrl   = string.IsNullOrEmpty(picture) ? null : picture,
            TenantId    = string.IsNullOrEmpty(orgId)   ? null : orgId,
            Roles       = roles,
            Abilities   = [],
        };
    }

    /// <summary>
    /// Extracts user profile from the id_token JWT claims.
    /// Decodes the payload without signature verification (token already trusted
    /// since it came directly from Logto's token endpoint over HTTPS).
    /// </summary>
    private static UserProfile? ExtractProfileFromIdToken(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;

            // Pad base64url to standard base64
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "=";  break;
            }

            var bytes = Convert.FromBase64String(payload);
            var json  = JsonDocument.Parse(bytes).RootElement;

            return MapToUserProfile(json);
        }
        catch
        {
            return null;
        }
    }

    private static UserProfile MapToUserProfile(JsonElement userInfo)
    {
        var roles = new List<string>();
        if (userInfo.TryGetProperty("roles", out var rolesEl))
            foreach (var r in rolesEl.EnumerateArray())
                if (r.GetString() is string role) roles.Add(role);

        var name = userInfo.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var parts = name.Split(' ', 2);

        return new UserProfile
        {
            Id          = userInfo.TryGetProperty("sub", out var sub) ? sub.GetString()! : string.Empty,
            LoginName   = userInfo.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
            FirstName   = parts.Length > 0 ? parts[0] : "",
            LastName    = parts.Length > 1 ? parts[1] : "",
            Email       = userInfo.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "",
            AvatarUrl   = userInfo.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
            TenantId    = userInfo.TryGetProperty("organization_id", out var org) ? org.GetString() : null,
            Roles       = roles,
            Abilities   = [],
        };
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private void EnforceMaxCacheSize()
    {
        if (_tokenCache.Count < _cacheOpts.TokenCacheMaxSize) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _tokenCache.Keys.ToList())
            if (_tokenCache.TryGetValue(key, out var e) && e.EvictAt <= now)
                _tokenCache.TryRemove(key, out _);
        if (_tokenCache.Count >= _cacheOpts.TokenCacheMaxSize)
            foreach (var key in _tokenCache.OrderBy(e => e.Value.CachedAt)
                .Take(_tokenCache.Count - _cacheOpts.TokenCacheMaxSize + 1)
                .Select(e => e.Key))
                _tokenCache.TryRemove(key, out _);
    }

    private static (string Code, string Message) ClassifyException(Exception? ex) => ex switch
    {
        SecurityTokenExpiredException          => (GateErrorCodes.TokenExpired,    "Token has expired."),
        SecurityTokenInvalidSignatureException => (GateErrorCodes.InvalidToken,    "Invalid token signature."),
        SecurityTokenInvalidAudienceException  => (GateErrorCodes.InvalidToken,    "Invalid token audience."),
        SecurityTokenInvalidIssuerException    => (GateErrorCodes.InvalidToken,    "Invalid token issuer."),
        _                                      => (GateErrorCodes.InvalidToken,    ex?.Message ?? "Token validation failed."),
    };
}
