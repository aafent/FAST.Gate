// FastGateConfig.cs
namespace FAST.Gate.Client;

public sealed class FastGateConfig
{
    public string GateBaseUrl { get; set; } = string.Empty;
    public string SsoRedirectUri { get; set; } = string.Empty;

    public int TokenRefreshBufferSeconds { get; set; } = 60;
    public int TimeoutMs { get; set; } = 10000;
    public bool PropagateCorrelationId { get; set; } = true;
}