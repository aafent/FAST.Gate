using FAST.Gate.Client.Abstractions;
using FAST.Gate.Configuration;
using FAST.Gate.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Auth;

/// <summary>
/// Registers all FAST.Gate authentication and authorization endpoints.
///
/// Endpoints:
///   POST /auth/refresh           — silent token refresh
///   GET  /auth/validate          — validate inbound Bearer token
///   POST /auth/logout            — revoke tokens and end session
///   GET  /auth/me                — get current user profile
///   GET  /auth/sso/url           — get SSO redirect URL
///   POST /auth/sso/exchange      — exchange SSO auth code for tokens
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapFastGateAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/refresh",      RefreshHandler);
        app.MapGet ("/auth/validate",     ValidateHandler);
        app.MapPost("/auth/logout",       LogoutHandler);
        app.MapGet ("/auth/me",           MeHandler);
        app.MapGet ("/auth/sso/url",      SsoUrlHandler);
        app.MapPost("/auth/sso/exchange", SsoExchangeHandler);

        return app;
    }

    // ── POST /auth/refresh ────────────────────────────────────────────────────

    private static async Task<IResult> RefreshHandler(
        RefreshRequest request,
        IIdentityProvider idp,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Results.Json(
                FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.MissingToken,
                    "RefreshToken is required.",
                    GetRequestId(http))),
                statusCode: 400);

        var result = await idp.RefreshTokenAsync(request.RefreshToken, http.RequestAborted);

        return result.IsSuccess
            ? Results.Ok(result)
            : Results.Json(result, statusCode: GetHttpStatus(result.Error));
    }

    // ── GET /auth/validate ────────────────────────────────────────────────────

    private static async Task<IResult> ValidateHandler(
        IIdentityProvider idp,
        HttpContext http)
    {
        var token = ExtractBearerToken(http);
        if (token is null)
            return Results.Json(
                FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.MissingToken,
                    "Missing Authorization: Bearer header.",
                    GetRequestId(http))),
                statusCode: 401);

        var result = await idp.ValidateTokenAsync(token, http.RequestAborted);

        return result.IsSuccess
            ? Results.Ok(result)
            : Results.Json(result, statusCode: GetHttpStatus(result.Error));
    }

    // ── POST /auth/logout ─────────────────────────────────────────────────────

    private static async Task<IResult> LogoutHandler(
        LogoutRequest request,
        IIdentityProvider idp,
        HttpContext http)
    {
        var token = ExtractBearerToken(http) ?? request.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(
                GateError.Create(
                    GateErrorCodes.MissingToken,
                    "AccessToken is required.",
                    GetRequestId(http)),
                statusCode: 400);

        await idp.LogoutAsync(token, request.RefreshToken, http.RequestAborted);
        return Results.Ok(new { loggedOut = true });
    }

    // ── GET /auth/me ──────────────────────────────────────────────────────────

    private static async Task<IResult> MeHandler(
        IIdentityProvider idp,
        HttpContext http)
    {
        var token = ExtractBearerToken(http);
        if (token is null)
            return Results.Json(
                GateError.Create(
                    GateErrorCodes.MissingToken,
                    "Missing Authorization: Bearer header.",
                    GetRequestId(http)),
                statusCode: 401);

        // Validate first to ensure token is still good
        var validation = await idp.ValidateTokenAsync(token, http.RequestAborted);
        if (!validation.IsSuccess)
            return Results.Json(validation.Error, statusCode: GetHttpStatus(validation.Error));

        var profile = await idp.GetUserProfileAsync(token, http.RequestAborted);
        return profile is not null
            ? Results.Ok(profile)
            : Results.Json(
                GateError.Create(GateErrorCodes.ProviderError,
                    "Could not retrieve user profile.", GetRequestId(http)),
                statusCode: 502);
    }

    // ── GET /auth/sso/url ─────────────────────────────────────────────────────

    private static IResult SsoUrlHandler(
        IIdentityProvider idp,
        IOptions<GateOptions> options,
        HttpContext http,
        string? redirectUri = null,
        string? tenantId = null)
    {
        var redirect = redirectUri ?? string.Empty;

        // Validate redirect URI is in the allowed list
        if (!options.Value.Sso.AllowedRedirectUris.Contains(redirect))
            return Results.Json(
                GateError.Create(GateErrorCodes.InternalError,
                    $"Redirect URI '{redirect}' is not in the allowed list.", GetRequestId(http)),
                statusCode: 400);

        var state = Guid.NewGuid().ToString("N");
        var url = idp.GetSsoLoginUrl(redirect, state, tenantId);

        return Results.Ok(new { url, state });
    }

    // ── POST /auth/sso/exchange ───────────────────────────────────────────────

    private static async Task<IResult> SsoExchangeHandler(
        SsoExchangeRequest request,
        IIdentityProvider idp,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.Json(
                FastAuthResult.Failure(GateError.Create(
                    GateErrorCodes.MissingCredentials,
                    "SSO code is required.",
                    GetRequestId(http))),
                statusCode: 400);

        var result = await idp.ExchangeSsoCodeAsync(
            request.Code,
            request.RedirectUri ?? string.Empty,
            http.RequestAborted);

        return result.IsSuccess
            ? Results.Ok(result)
            : Results.Json(result, statusCode: GetHttpStatus(result.Error));
    }


    // ── Error code to HTTP status mapping ─────────────────────────────────────

    private static int GetHttpStatus(GateError? error) => error?.Code switch
    {
        GateErrorCodes.MissingCredentials   => 400,
        GateErrorCodes.InvalidCredentials   => 401,
        GateErrorCodes.AccountDisabled      => 403,
        GateErrorCodes.MissingToken         => 401,
        GateErrorCodes.InvalidToken         => 401,
        GateErrorCodes.TokenExpired         => 401,
        GateErrorCodes.RefreshTokenExpired  => 401,
        GateErrorCodes.RoleDenied           => 403,
        GateErrorCodes.AbilityDenied        => 403,
        GateErrorCodes.TenantNotFound       => 404,
        GateErrorCodes.TenantAccessDenied   => 403,
        GateErrorCodes.ProviderUnavailable  => 503,
        GateErrorCodes.ProviderError        => 502,
        _                                   => 500,
    };
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ExtractBearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.FirstOrDefault();
        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header["Bearer ".Length..]
            : null;
    }

    private static string GetRequestId(HttpContext http) =>
        http.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var id)
            ? id?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
}

// ── Request models ────────────────────────────────────────────────────────────

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(
    string? AccessToken = null,
    string? RefreshToken = null);

public sealed record SsoExchangeRequest(
    string Code,
    string? RedirectUri = null);
