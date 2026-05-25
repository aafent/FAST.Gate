using FAST.Gate.Client.Abstractions;
using FluentAssertions;

namespace FAST.Gate.Client.Tests;

public sealed class GateErrorTests
{
    [Fact]
    public void Create_PopulatesAllFields()
    {
        var error = GateError.Create(
            GateErrorCodes.InvalidCredentials,
            "Invalid username or password.",
            "req-123",
            "Check your caps lock.");

        error.Code.Should().Be(GateErrorCodes.InvalidCredentials);
        error.Message.Should().Be("Invalid username or password.");
        error.RequestId.Should().Be("req-123");
        error.Detail.Should().Be("Check your caps lock.");
        error.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AllErrorCodes_StartWithGatePrefix()
    {
        var codes = new[]
        {
            GateErrorCodes.MissingCredentials,
            GateErrorCodes.InvalidCredentials,
            GateErrorCodes.AccountDisabled,
            GateErrorCodes.MissingToken,
            GateErrorCodes.InvalidToken,
            GateErrorCodes.TokenExpired,
            GateErrorCodes.RefreshTokenExpired,
            GateErrorCodes.RoleDenied,
            GateErrorCodes.AbilityDenied,
            GateErrorCodes.TenantNotFound,
            GateErrorCodes.TenantAccessDenied,
            GateErrorCodes.ProviderUnavailable,
            GateErrorCodes.ProviderError,
            GateErrorCodes.InternalError,
        };

        codes.Should().AllSatisfy(c => c.Should().StartWith("gate_"));
    }
}
