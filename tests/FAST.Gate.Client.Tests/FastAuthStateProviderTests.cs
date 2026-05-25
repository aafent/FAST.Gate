using FAST.Gate.Client.Abstractions;
using FAST.Gate.Client.Auth;
using FAST.Gate.Client.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FAST.Gate.Client.Tests;

public sealed class FastAuthStateProviderTests
{
    private static FastAuthStateProvider CreateProvider(int bufferSeconds = 60)
    {
        var opts = Options.Create(new GateClientOptions
        {
            GateBaseUrl = "https://gate.fast.internal",
            TokenRefreshBufferSeconds = bufferSeconds,
        });
        return new FastAuthStateProvider(opts);
    }

    private static UserProfile SampleUser() => new()
    {
        Id        = "user-1",
        LoginName = "jdoe",
        FirstName = "John",
        LastName  = "Doe",
        Email     = "jdoe@fast.internal",
        Roles     = ["erp.admin"],
    };

    private static FastAuthResult ValidResult() =>
        FastAuthResult.Success(
            SampleUser(),
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15));

    [Fact]
    public async Task GetAuthenticationStateAsync_BeforeLogin_ReturnsAnonymous()
    {
        var provider = CreateProvider();
        var state = await provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task NotifyAuthSuccess_ValidResult_ReturnsAuthenticated()
    {
        var provider = CreateProvider();
        provider.NotifyAuthSuccess(ValidResult());

        var state = await provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.Identity.Name.Should().Be("John Doe");
    }

    [Fact]
    public async Task NotifyAuthSuccess_SetsRolesClaims()
    {
        var provider = CreateProvider();
        provider.NotifyAuthSuccess(ValidResult());

        var state = await provider.GetAuthenticationStateAsync();
        state.User.IsInRole("erp.admin").Should().BeTrue();
    }

    [Fact]
    public async Task NotifyLogout_ClearsState_ReturnsAnonymous()
    {
        var provider = CreateProvider();
        provider.NotifyAuthSuccess(ValidResult());
        provider.NotifyLogout();

        var state = await provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_ExpiredToken_ReturnsAnonymous()
    {
        var provider = CreateProvider(bufferSeconds: 0);
        var expired = FastAuthResult.Success(
            SampleUser(), "token", "refresh",
            DateTimeOffset.UtcNow.AddSeconds(-1)); // already expired

        provider.NotifyAuthSuccess(expired);

        var state = await provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void GetAccessToken_ValidToken_ReturnsToken()
    {
        var provider = CreateProvider();
        provider.NotifyAuthSuccess(ValidResult());

        provider.GetAccessToken().Should().Be("access-token");
    }

    [Fact]
    public void GetAccessToken_NoAuth_ReturnsNull()
    {
        var provider = CreateProvider();
        provider.GetAccessToken().Should().BeNull();
    }

    [Fact]
    public void GetAccessToken_ExpiredToken_ReturnsNull()
    {
        var provider = CreateProvider(bufferSeconds: 0);
        var expired = FastAuthResult.Success(
            SampleUser(), "token", "refresh",
            DateTimeOffset.UtcNow.AddSeconds(-1));

        provider.NotifyAuthSuccess(expired);
        provider.GetAccessToken().Should().BeNull();
    }

    [Fact]
    public void CurrentAuth_AfterLogin_ReturnsResult()
    {
        var provider = CreateProvider();
        var result = ValidResult();
        provider.NotifyAuthSuccess(result);

        provider.CurrentAuth.Should().Be(result);
    }

    [Fact]
    public void CurrentAuth_AfterLogout_ReturnsNull()
    {
        var provider = CreateProvider();
        provider.NotifyAuthSuccess(ValidResult());
        provider.NotifyLogout();

        provider.CurrentAuth.Should().BeNull();
    }
}
