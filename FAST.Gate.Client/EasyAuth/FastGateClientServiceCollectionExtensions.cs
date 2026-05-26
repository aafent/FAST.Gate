// FastGateClientServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FAST.Gate.Client;

public static class FastGateClientServiceCollectionExtensions
{
    public static IServiceCollection AddFastGateClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configSectionName = "FastGate")
    {
        var section = configuration.GetSection(configSectionName);
        var cfg = section.Get<FastGateConfig>() ?? new FastGateConfig();
        services.AddSingleton(cfg);

        services.AddSingleton<FastGateSession>();
        services.AddScoped<IFastGateSession>(sp => sp.GetRequiredService<FastGateSession>());

        services.AddHttpClient<IFastGateAuthService, FastGateAuthService>((sp, client) =>
        {
            var config = sp.GetRequiredService<FastGateConfig>();
            client.Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs);
            client.BaseAddress = new Uri(config.GateBaseUrl);
        });

        return services;
    }
}