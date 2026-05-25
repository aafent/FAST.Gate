namespace FAST.Gate.Client.Abstractions;

/// <summary>
/// Core abstraction over any identity provider (Logto, Auth0, Keycloak, etc.).
/// FAST.Gate depends only on this interface. FAST applications never see it —
/// they interact exclusively with FAST.Gate's endpoints.
///
/// All authentication and authorization for the FAST ecosystem flows through here.
/// </summary>
public interface IIdentityProvider
{
    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once at startup. Pre-fetches JWKS, warms caches, validates config.
    /// Throws if the provider is misconfigured or unreachable.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Called on graceful shutdown.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);

    // ── Authentication ────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates a user with username and password.
    /// Returns a full <see cref="FastAuthResult"/> including user profile, tokens, and roles.
    /// </summary>
    Task<FastAuthResult> LoginAsync(
        string loginName,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Silently refreshes an expired or near-expiry access token using the refresh token.
    /// Returns a new <see cref="FastAuthResult"/> with <c>TokenRefreshed = true</c>.
    /// Returns failure if the refresh token is itself expired — caller must re-login.
    /// </summary>
    Task<FastAuthResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an inbound access token (from a FAST application request).
    /// Used by FAST.Gate to authenticate service-to-service calls.
    /// Implements per-instance in-memory caching keyed on SHA-256(token).
    /// </summary>
    Task<FastAuthResult> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out a user by revoking their tokens at the IdP.
    /// </summary>
    Task<bool> LogoutAsync(
        string accessToken,
        string? refreshToken = null,
        CancellationToken cancellationToken = default);

    // ── User profile ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the full user profile for an already-authenticated user.
    /// Uses the access token to call the IdP's userinfo endpoint.
    /// </summary>
    Task<UserProfile?> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    // ── Authorization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the user profile contains the specified role.
    /// Checks both global and app-specific roles.
    /// </summary>
    bool HasRole(UserProfile user, string role);

    /// <summary>
    /// Returns true if the user profile contains the specified ability.
    /// </summary>
    bool HasAbility(UserProfile user, string ability);

    // ── SSO ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the SSO login URL for redirect-based flows (Blazor WASM, web apps).
    /// The user is redirected to this URL, authenticates at the IdP,
    /// then redirected back to <paramref name="redirectUri"/> with an auth code.
    /// </summary>
    string GetSsoLoginUrl(string redirectUri, string state, string? tenantId = null);

    /// <summary>
    /// Exchanges the auth code received from the SSO callback for a full <see cref="FastAuthResult"/>.
    /// </summary>
    Task<FastAuthResult> ExchangeSsoCodeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    // ── Health ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reports whether the provider backend is healthy and reachable.
    /// Fed into the /health endpoint and the load balancer probe.
    /// </summary>
    Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken = default);
}
