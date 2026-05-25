using FAST.Gate.Client.Auth;
using FAST.Gate.Client.Configuration;
using FAST.Gate.Client.Middleware;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Client.Extensions;

/// <summary>
/// Extension methods to register FAST.Gate.Client into a FAST application's DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all FAST.Gate client services.
    ///
    /// Usage in any FAST application's Program.cs:
    /// <code>
    /// builder.Services.AddFastGateClient(builder.Configuration);
    /// </code>
    ///
    /// This registers:
    ///   - <see cref="GateAuthClient"/> — inject to perform login, logout, refresh, validate
    ///   - <see cref="FastAuthStateProvider"/> — wired as Blazor's AuthenticationStateProvider
    ///   - Correlation ID propagation on all outgoing calls
    /// </summary>
    public static IServiceCollection AddFastGateClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<GateClientOptions>()
            .Bind(configuration.GetSection(GateClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ICorrelationIdAccessor, DefaultCorrelationIdAccessor>();
        services.AddTransient<CorrelationIdHandler>();

        services
            .AddHttpClient<GateAuthClient>((sp, client) =>
            {
                var opts = configuration
                    .GetSection(GateClientOptions.SectionName)
                    .Get<GateClientOptions>() ?? new GateClientOptions();

                if (!string.IsNullOrWhiteSpace(opts.GateBaseUrl))
                    client.BaseAddress = new Uri(opts.GateBaseUrl);

                client.Timeout = TimeSpan.FromMilliseconds(opts.TimeoutMs);
            })
            .AddHttpMessageHandler<CorrelationIdHandler>();

        // Blazor auth state
        services.AddScoped<FastAuthStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<FastAuthStateProvider>());

        services.AddAuthorizationCore();

        return services;
    }

    /// <summary>
    /// Overload allowing a custom <see cref="ICorrelationIdAccessor"/> —
    /// use in ASP.NET Core hosts to propagate the inbound X-Request-Id.
    /// </summary>
    public static IServiceCollection AddFastGateClient(
        this IServiceCollection services,
        IConfiguration configuration,
        ICorrelationIdAccessor accessor)
    {
        services.AddSingleton(accessor);
        return AddFastGateClient(services, configuration);
    }
}
