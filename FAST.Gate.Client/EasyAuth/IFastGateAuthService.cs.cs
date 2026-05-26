// IFastGateAuthService.cs
namespace FAST.Gate.Client;

public interface IFastGateAuthService
{
    Task<string> GetLoginUrlAsync(CancellationToken cancellationToken = default);

    Task<FastLoginResult> CompleteLoginAsync(
        string code,
        string? state,
        CancellationToken cancellationToken = default);

    Task EnsureFreshAsync(CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}