// IFastGateSession.cs
namespace FAST.Gate.Client;

public interface IFastGateSession
{
    bool IsAuthenticated { get; }
    DateTimeOffset? ExpiresAt { get; }

    FastUserInfo? User { get; }

    string? AccessToken { get; }      // API token for downstream calls
    string? RefreshToken { get; }     // Not exposed to UI apps normally, but available if needed
}