namespace FAST.Gate.Client.Abstractions;

/// <summary>
/// The unified authentication response returned by FAST.Gate to every FAST application.
/// FAST applications only ever deal with this type — never with Logto-specific tokens or objects.
///
/// On success: IsSuccess=true, User and tokens are populated, Error is null.
/// On failure: IsSuccess=false, Error is populated, all other fields are null/default.
/// </summary>
public sealed record FastAuthResult
{
    /// <summary>Whether authentication succeeded.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Whether the underlying IdP token was silently refreshed during this operation.
    /// FAST applications can use this to update stored tokens without re-authenticating.
    /// </summary>
    public bool TokenRefreshed { get; init; }

    /// <summary>
    /// FAST ecosystem token.
    /// Reserved for future use — empty string for now.
    /// Will become FAST.Gate's own issued token, wrapping the IdP token.
    /// FAST applications should store this and pass it on future calls.
    /// </summary>
    public string FastToken { get; init; } = string.Empty;

    /// <summary>The raw IdP access token (Logto JWT). Used internally by FAST.Gate.</summary>
    public string? IdpAccessToken { get; init; }

    /// <summary>The IdP refresh token. Stored securely — never exposed to the browser.</summary>
    public string? IdpRefreshToken { get; init; }

    /// <summary>When the access token expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Authenticated user's profile. Null on failure.</summary>
    public UserProfile? User { get; init; }

    /// <summary>
    /// Error details. Null on success.
    /// Uses the same <see cref="GateError"/> envelope used across all FAST.Gate responses.
    /// </summary>
    public GateError? Error { get; init; }

    // ── Factory methods ───────────────────────────────────────────────────────

    public static FastAuthResult Success(
        UserProfile user,
        string idpAccessToken,
        string idpRefreshToken,
        DateTimeOffset expiresAt,
        bool tokenRefreshed = false) => new()
    {
        IsSuccess = true,
        TokenRefreshed = tokenRefreshed,
        User = user,
        IdpAccessToken = idpAccessToken,
        IdpRefreshToken = idpRefreshToken,
        ExpiresAt = expiresAt,
    };

    public static FastAuthResult Failure(GateError error) => new()
    {
        IsSuccess = false,
        Error = error,
    };
}
