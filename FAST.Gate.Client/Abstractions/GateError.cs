namespace FAST.Gate.Client.Abstractions;

/// <summary>
/// Normalised error envelope for all FAST.Gate error responses.
/// Replaces the old ProxyError — now covers authentication, authorization,
/// and all other gate-level errors.
/// </summary>
public sealed record GateError
{
    /// <summary>Machine-readable error code. Always one of <see cref="GateErrorCodes"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Correlation ID — matches X-Request-Id header and all log entries for this request.
    /// Use this to trace the exact failure in FAST.Gate logs.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>UTC timestamp of the error.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional additional detail — populated for validation errors.</summary>
    public string? Detail { get; init; }

    public static GateError Create(string code, string message, string requestId, string? detail = null) =>
        new() { Code = code, Message = message, RequestId = requestId, Detail = detail };
}

/// <summary>
/// Closed set of machine-readable FAST.Gate error codes.
/// </summary>
public static class GateErrorCodes
{
    // ── Authentication ────────────────────────────────────────────────────────
    /// <summary>Credentials missing from the request.</summary>
    public const string MissingCredentials     = "gate_missing_credentials";

    /// <summary>Username or password is incorrect.</summary>
    public const string InvalidCredentials     = "gate_invalid_credentials";

    /// <summary>Account is locked or disabled.</summary>
    public const string AccountDisabled        = "gate_account_disabled";

    /// <summary>Token is missing from the request.</summary>
    public const string MissingToken           = "gate_missing_token";

    /// <summary>Token failed signature or structural validation.</summary>
    public const string InvalidToken           = "gate_invalid_token";

    /// <summary>Token has expired.</summary>
    public const string TokenExpired           = "gate_token_expired";

    /// <summary>Refresh token is invalid or expired — full re-login required.</summary>
    public const string RefreshTokenExpired    = "gate_refresh_token_expired";

    // ── Authorization ─────────────────────────────────────────────────────────
    /// <summary>Authenticated user lacks the required role.</summary>
    public const string RoleDenied             = "gate_role_denied";

    /// <summary>Authenticated user lacks the required ability.</summary>
    public const string AbilityDenied          = "gate_ability_denied";

    // ── Tenant ────────────────────────────────────────────────────────────────
    /// <summary>Tenant not found or not active.</summary>
    public const string TenantNotFound         = "gate_tenant_not_found";

    /// <summary>User does not belong to the requested tenant.</summary>
    public const string TenantAccessDenied     = "gate_tenant_access_denied";

    // ── Provider ──────────────────────────────────────────────────────────────
    /// <summary>The identity provider (Logto) is unreachable.</summary>
    public const string ProviderUnavailable    = "gate_provider_unavailable";

    /// <summary>Unexpected error from the identity provider.</summary>
    public const string ProviderError          = "gate_provider_error";

    // ── General ───────────────────────────────────────────────────────────────
    /// <summary>Unexpected internal error in FAST.Gate.</summary>
    public const string InternalError          = "gate_internal_error";
}
