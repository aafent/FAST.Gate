// FastGateSession.cs
namespace FAST.Gate.Client;

internal sealed class FastGateSession : IFastGateSession
{
    public bool IsAuthenticated { get; internal set; }
    public DateTimeOffset? ExpiresAt { get; internal set; }
    public FastUserInfo? User { get; internal set; }
    public string? AccessToken { get; internal set; }
    public string? RefreshToken { get; internal set; }
}