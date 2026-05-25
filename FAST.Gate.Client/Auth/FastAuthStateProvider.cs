using FAST.Gate.Client.Abstractions;
using FAST.Gate.Client.Configuration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FAST.Gate.Client.Auth;

/// <summary>
/// Blazor AuthenticationStateProvider backed by FAST.Gate.
///
/// Works for both Blazor Server and Blazor WASM — state is held in-memory
/// and updated via <see cref="NotifyAuthSuccess"/> and <see cref="NotifyLogout"/>.
///
/// FAST applications inject <see cref="GateAuthClient"/> to perform auth operations,
/// then call NotifyAuthSuccess/NotifyLogout to update Blazor's auth state.
///
/// Usage:
/// <code>
/// @inject GateAuthClient GateAuth
/// @inject FastAuthStateProvider AuthState
///
/// var result = await GateAuth.LoginAsync(username, password);
/// if (result.IsSuccess) AuthState.NotifyAuthSuccess(result);
/// </code>
/// </summary>
public sealed class FastAuthStateProvider : AuthenticationStateProvider
{
    private readonly GateClientOptions _options;

    private FastAuthResult? _current;

    private static readonly AuthenticationState _anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public FastAuthStateProvider(IOptions<GateClientOptions> options)
    {
        _options = options.Value;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_current is null || !_current.IsSuccess || _current.User is null)
            return Task.FromResult(_anonymous);

        var buffer = TimeSpan.FromSeconds(_options.TokenRefreshBufferSeconds);
        // If no expiry info, treat as expired — force re-authentication
        if (!_current.ExpiresAt.HasValue || DateTimeOffset.UtcNow >= _current.ExpiresAt.Value - buffer)
            return Task.FromResult(_anonymous);

        return Task.FromResult(BuildState(_current.User));
    }

    /// <summary>
    /// Call after a successful login or SSO exchange to update Blazor auth state.
    /// </summary>
    public void NotifyAuthSuccess(FastAuthResult result)
    {
        _current = result;
        NotifyAuthenticationStateChanged(
            Task.FromResult(result.User is not null
                ? BuildState(result.User)
                : _anonymous));
    }

    /// <summary>
    /// Call after logout to clear Blazor auth state.
    /// </summary>
    public void NotifyLogout()
    {
        _current = null;
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }

    /// <summary>Returns the current FastAuthResult. Null if not authenticated.</summary>
    public FastAuthResult? CurrentAuth => _current;

    /// <summary>
    /// Returns the current access token if authenticated and not near expiry.
    /// Returns null if unauthenticated or token needs refresh.
    /// </summary>
    public string? GetAccessToken()
    {
        if (_current is null || !_current.IsSuccess) return null;
        var buffer = TimeSpan.FromSeconds(_options.TokenRefreshBufferSeconds);
        if (!_current.ExpiresAt.HasValue || DateTimeOffset.UtcNow >= _current.ExpiresAt.Value - buffer)
            return null;
        return _current.IdpAccessToken;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuthenticationState BuildState(UserProfile user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name,           user.DisplayName),
            new(ClaimTypes.GivenName,      user.FirstName),
            new(ClaimTypes.Surname,        user.LastName),
            new(ClaimTypes.Email,          user.Email),
        };

        if (user.TenantId is not null)
            claims.Add(new("tenant_id", user.TenantId));

        if (user.AvatarUrl is not null)
            claims.Add(new("avatar_url", user.AvatarUrl));

        foreach (var role in user.Roles)
            claims.Add(new(ClaimTypes.Role, role));

        foreach (var ability in user.Abilities)
            claims.Add(new("ability", ability));

        var identity = new ClaimsIdentity(claims, "FastGate");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
