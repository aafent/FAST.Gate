using System.ComponentModel.DataAnnotations;

namespace FAST.Gate.Configuration;

/// <summary>Root configuration for FAST.Gate. Bound from appsettings.json "Gate" section.</summary>
public sealed class GateOptions
{
    public const string SectionName = "Gate";

    public IdentityOptions Identity { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
    public SsoOptions Sso { get; set; } = new();
    public SessionOptions Session { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public sealed class IdentityOptions
{
    /// <summary>Which IdP provider to use. Currently "logto". Extensible via factory.</summary>
    [Required]
    public string Provider { get; set; } = "logto";

    public LogtoOptions Logto { get; set; } = new();
}

public sealed class LogtoOptions
{
    /// <summary>OIDC issuer URL e.g. https://2uaw9x.logto.app/oidc</summary>
    [Required]
    [Url]
    public string IssuerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Logto Management/Experience API base URL e.g. https://2uaw9x.logto.app
    /// Used for direct username/password login via the Logto Experience API.
    /// Derived from IssuerUrl if not set explicitly.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Returns the effective API base URL — ApiBaseUrl if set, otherwise IssuerUrl with /oidc stripped.</summary>
    public string GetApiBaseUrl() =>
        !string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? ApiBaseUrl.TrimEnd('/')
            : IssuerUrl.Replace("/oidc", string.Empty).TrimEnd('/');

    /// <summary>The API resource identifier registered in Logto for FAST.Gate.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>FAST.Gate's own M2M client ID in Logto.</summary>
    public string ServiceClientId { get; set; } = string.Empty;

    /// <summary>FAST.Gate's own M2M client secret in Logto.</summary>
    public string ServiceClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Client ID of the Traditional Web application in Logto.
    /// Used for the SSO authorization code flow (browser-based login).
    /// This is DIFFERENT from ServiceClientId which is the M2M app.
    /// </summary>
    public string WebClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client Secret of the Traditional Web application in Logto.
    /// Used for the SSO authorization code flow.
    /// </summary>
    public string WebClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered in Logto for the Traditional Web app.
    /// Must match exactly what is registered in Logto.
    /// e.g. https://gate.fast.internal/auth/callback
    /// </summary>
    public string ServiceRedirectUri { get; set; } = string.Empty;

    /// <summary>How long to cache JWKS keys. Default: 10 minutes.</summary>
    public int JwksCacheTtlMs { get; set; } = 600_000;

    /// <summary>Timeout for JWKS endpoint fetch. Default: 3 seconds.</summary>
    public int JwksFetchTimeoutMs { get; set; } = 3_000;

    /// <summary>Retry attempts on JWKS fetch failure. Default: 3.</summary>
    public int JwksRetryAttempts { get; set; } = 3;

    /// <summary>Delay between JWKS retries. Default: 500ms.</summary>
    public int JwksRetryDelayMs { get; set; } = 500;
}

public sealed class CacheOptions
{
    /// <summary>Master switch for token validation caching. Disable in dev.</summary>
    public bool TokenCacheEnabled { get; set; } = true;

    /// <summary>Max validated tokens held per instance. Default: 1000.</summary>
    public int TokenCacheMaxSize { get; set; } = 1_000;

    /// <summary>Evict tokens this many ms before their exp. Default: 10 seconds.</summary>
    public int TokenCacheEarlyExpiryBufferMs { get; set; } = 10_000;
}

public sealed class SsoOptions
{
    /// <summary>
    /// List of allowed redirect URIs for SSO callbacks.
    /// Each FAST application registers its callback URL here.
    /// </summary>
    public List<string> AllowedRedirectUris { get; set; } = [];
}

public sealed class SessionOptions
{
    /// <summary>Default session lifetime in minutes. Default: 60 minutes.</summary>
    public int SessionLifetimeMinutes { get; set; } = 60;

    /// <summary>Remember-me session lifetime in days. Default: 30 days.</summary>
    public int RememberMeLifetimeDays { get; set; } = 30;

    /// <summary>How many seconds before expiry to proactively refresh. Default: 60 seconds.</summary>
    public int RefreshBufferSeconds { get; set; } = 60;
}

public sealed class LoggingOptions
{
    /// <summary>Minimum log level. Default: Information.</summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>Fields always redacted from logs. Tokens and passwords are always redacted regardless.</summary>
    public List<string> RedactedFields { get; set; } = ["Password", "AccessToken", "RefreshToken"];
}

