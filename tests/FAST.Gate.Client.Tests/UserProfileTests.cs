using FAST.Gate.Client.Abstractions;
using FluentAssertions;

namespace FAST.Gate.Client.Tests;

public sealed class UserProfileTests
{
    [Fact]
    public void DisplayName_CombinesFirstAndLastName()
    {
        var user = new UserProfile
        {
            Id        = "1",
            LoginName = "jdoe",
            FirstName = "John",
            LastName  = "Doe",
            Email     = "jdoe@fast.internal",
            Roles     = [],
        };

        user.DisplayName.Should().Be("John Doe");
    }

    [Fact]
    public void DisplayName_OnlyFirstName_NoTrailingSpace()
    {
        var user = new UserProfile
        {
            Id        = "1",
            LoginName = "jdoe",
            FirstName = "John",
            LastName  = string.Empty,
            Email     = "jdoe@fast.internal",
            Roles     = [],
        };

        user.DisplayName.Should().Be("John");
    }

    [Fact]
    public void Abilities_DefaultsToEmptyCollection()
    {
        var user = new UserProfile
        {
            Id        = "1",
            LoginName = "jdoe",
            FirstName = "John",
            LastName  = "Doe",
            Email     = "jdoe@fast.internal",
            Roles     = [],
        };

        user.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Roles_And_Abilities_AreReadOnly()
    {
        var user = new UserProfile
        {
            Id        = "1",
            LoginName = "jdoe",
            FirstName = "John",
            LastName  = "Doe",
            Email     = "jdoe@fast.internal",
            Roles     = ["erp.admin", "fast.flowchart.user"],
            Abilities = ["erp.accounts.delete"],
        };

        user.Roles.Should().ContainInOrder("erp.admin", "fast.flowchart.user");
        user.Abilities.Should().Contain("erp.accounts.delete");
    }

    [Fact]
    public void AvatarUrl_IsOptional()
    {
        var user = new UserProfile
        {
            Id        = "1",
            LoginName = "jdoe",
            FirstName = "John",
            LastName  = "Doe",
            Email     = "jdoe@fast.internal",
            Roles     = [],
            AvatarUrl = null,
        };

        user.AvatarUrl.Should().BeNull();
    }
}
