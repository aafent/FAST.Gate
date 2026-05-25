using FAST.Gate.Client.Abstractions;
using FAST.Gate.Configuration;
using FAST.Gate.Providers.Identity.Logto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Providers.Identity;

/// <summary>
/// Resolves the active <see cref="IIdentityProvider"/> implementation
/// based on <see cref="IdentityOptions.Provider"/> configuration.
///
/// Adding a new provider:
///   1. Implement <see cref="IIdentityProvider"/>
///   2. Add a case here
///   3. Zero changes to FAST.Gate core or any FAST application
/// </summary>
public static class IdentityProviderFactory
{
    public static IIdentityProvider Create(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<GateOptions>>().Value;

        return opts.Identity.Provider.ToLowerInvariant() switch
        {
            "logto" => sp.GetRequiredService<LogtoIdentityProvider>(),
            // "auth0"     => sp.GetRequiredService<Auth0IdentityProvider>(),
            // "keycloak"  => sp.GetRequiredService<KeycloakIdentityProvider>(),
            // "cognito"   => sp.GetRequiredService<CognitoIdentityProvider>(),
            var unknown => throw new InvalidOperationException(
                $"Unknown identity provider: \"{unknown}\". Check Gate:Identity:Provider in configuration.")
        };
    }
}
