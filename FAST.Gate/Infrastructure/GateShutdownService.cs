using FAST.Gate.Client.Abstractions;
using Serilog;

namespace FAST.Gate.Infrastructure;

/// <summary>
/// Hosted service that handles graceful shutdown of FAST.Gate components.
/// Replaces the fire-and-forget async lambda anti-pattern on ApplicationStopping.
/// StopAsync is properly awaited by the ASP.NET Core host during shutdown.
/// </summary>
public sealed class GateShutdownService : IHostedService
{
    private readonly IIdentityProvider _identityProvider;

    public GateShutdownService(IIdentityProvider identityProvider)
    {
        _identityProvider = identityProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _identityProvider.ShutdownAsync(cancellationToken);
        Log.CloseAndFlush();
    }
}
