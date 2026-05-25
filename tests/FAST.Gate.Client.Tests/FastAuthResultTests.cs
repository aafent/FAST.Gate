using FAST.Gate.Client.Abstractions;
using FluentAssertions;

namespace FAST.Gate.Client.Tests;

public sealed class FastAuthResultTests
{
    private static UserProfile SampleUser() => new()
    {
        Id          = "user-1",
        LoginName   = "jdoe",
        FirstName   = "John",
        LastName    = "Doe",
        Email       = "jdoe@fast.internal",
        TenantId    = "tenant-abc",
        Roles       = ["erp.admin"],
        Abilities   = [],
    };

    [Fact]
    public void Success_PopulatesAllFields()
    {
        var user      = SampleUser();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var result    = FastAuthResult.Success(user, "access-token", "refresh-token", expiresAt);

        result.IsSuccess.Should().BeTrue();
        result.User.Should().Be(user);
        result.IdpAccessToken.Should().Be("access-token");
        result.IdpRefreshToken.Should().Be("refresh-token");
        result.ExpiresAt.Should().Be(expiresAt);
        result.Error.Should().BeNull();
        result.FastToken.Should().BeEmpty();
        result.TokenRefreshed.Should().BeFalse();
    }

    [Fact]
    public void Failure_PopulatesError_LeavesUserNull()
    {
        var error  = GateError.Create(GateErrorCodes.InvalidCredentials, "Bad credentials.", "req-1");
        var result = FastAuthResult.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        result.User.Should().BeNull();
        result.IdpAccessToken.Should().BeNull();
        result.FastToken.Should().BeEmpty();
    }

    [Fact]
    public void TokenRefreshed_IsSetCorrectly()
    {
        var result = FastAuthResult.Success(
            SampleUser(), "token", "refresh", DateTimeOffset.UtcNow.AddMinutes(15),
            tokenRefreshed: true);

        result.TokenRefreshed.Should().BeTrue();
    }

    [Fact]
    public void FastToken_IsAlwaysReservedEmpty()
    {
        var success = FastAuthResult.Success(SampleUser(), "t", "r", DateTimeOffset.UtcNow.AddMinutes(15));
        var failure = FastAuthResult.Failure(GateError.Create(GateErrorCodes.InternalError, "err", "req"));

        success.FastToken.Should().BeEmpty();
        failure.FastToken.Should().BeEmpty();
    }
}
