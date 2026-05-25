using FAST.Gate.Client.Abstractions;
using FAST.Gate.Configuration;
using FAST.Gate.Providers.Identity;
using FAST.Gate.Providers.Identity.Logto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FAST.Gate.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all FAST.Gate services.
    /// Call from Program.cs: builder.Services.AddFastGate(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddFastGate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Configuration ─────────────────────────────────────────────────────
        services
            .AddOptions<GateOptions>()
            .Bind(configuration.GetSection(GateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Identity providers ────────────────────────────────────────────────
        services.AddSingleton<LogtoIdentityProvider>();
        services.AddSingleton<IIdentityProvider>(IdentityProviderFactory.Create);

        // ── HTTP clients ──────────────────────────────────────────────────────
        services.AddHttpClient("LogtoHttp");

        return services;
    }
}
