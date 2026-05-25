using FAST.Gate.Configuration;
using FAST.Gate.Providers.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Tests;

public sealed class IdentityProviderFactoryTests
{
    [Fact]
    public void Create_UnknownProvider_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.Configure<GateOptions>(opts => opts.Identity.Provider = "unknown-provider");
        var sp = services.BuildServiceProvider();

        var act = () => IdentityProviderFactory.Create(sp);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown-provider*");
    }
}
