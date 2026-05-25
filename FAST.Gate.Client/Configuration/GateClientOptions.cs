using System.ComponentModel.DataAnnotations;

namespace FAST.Gate.Client.Configuration;

/// <summary>
/// Configuration for the FAST.Gate client SDK.
/// Bind from appsettings.json under the "FastGate" section.
/// </summary>
public sealed class GateClientOptions
{
    public const string SectionName = "FastGate";

    /// <summary>Base URL of the FAST.Gate service (e.g. https://gate.fast.internal).</summary>
    [Required]
    [Url]
    public string GateBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The URI the IdP redirects back to after SSO login.
    /// Must be registered in the IdP (Logto) as an allowed redirect URI.
    /// e.g. https://myapp.fast.internal/auth/callback
    /// </summary>
    public string? SsoRedirectUri { get; set; }

    /// <summary>
    /// How many seconds before token expiry to proactively refresh.
    /// Default: 60 seconds.
    /// </summary>
    public int TokenRefreshBufferSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout for calls to FAST.Gate in milliseconds.
    /// Default: 10 seconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Whether to propagate X-Request-Id from inbound requests.
    /// Default: true.
    /// </summary>
    public bool PropagateCorrelationId { get; set; } = true;
}
