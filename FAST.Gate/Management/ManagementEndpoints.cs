using FAST.Gate.Client.Abstractions;
using FAST.Gate.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Management;

/// <summary>
/// Registers FAST.Gate management endpoints.
///
/// Endpoints:
///   GET /health   — load balancer probe + provider health report
/// </summary>
public static class ManagementEndpoints
{
    public static IEndpointRouteBuilder MapFastGateManagement(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", HealthHandler);
        return app;
    }

    private static async Task<IResult> HealthHandler(
        IIdentityProvider identityProvider,
        IOptions<GateOptions> options)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var idpHealth = await identityProvider.HealthCheckAsync(cts.Token);

        var response = new
        {
            status   = idpHealth.State.ToString().ToLower(),
            version  = typeof(ManagementEndpoints).Assembly.GetName().Version?.ToString() ?? "unknown",
            provider = options.Value.Identity.Provider,
            checks   = new[]
            {
                new
                {
                    component = idpHealth.Component,
                    state     = idpHealth.State.ToString().ToLower(),
                    detail    = idpHealth.Detail,
                    latencyMs = idpHealth.Latency?.TotalMilliseconds,
                    checkedAt = idpHealth.CheckedAt,
                }
            },
        };

        return idpHealth.State == HealthState.Unhealthy
            ? Results.Json(response, statusCode: 503)
            : Results.Ok(response);
    }
}
