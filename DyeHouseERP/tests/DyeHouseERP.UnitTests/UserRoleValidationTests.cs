using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Domain.Common;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 41: rights are stored per user as a flat list of permission names that the API checks as role
/// claims. A name that is not in <see cref="Permissions.All"/> grants nothing silently, so both write paths
/// must refuse it. Update already did; create did not - these tests pin both paths to the same rule.
/// </summary>
public class UserRoleValidationTests
{
    private static CreateUserCommand Create(params string[] roles) =>
        new("newuser", "Password123", "New User", roles.ToList());

    private static UpdateUserCommand Update(params string[] roles) =>
        new(Guid.NewGuid(), "Some User", roles.ToList());

    [Fact]
    public void Create_AcceptsKnownPermissions_AndTheAdminCatchAll()
    {
        new CreateUserCommandValidator()
            .Validate(Create(Permissions.Admin, Permissions.AuditView, Permissions.InventoryAdjust))
            .IsValid.Should().BeTrue();

        // No roles at all is a valid (though powerless) login.
        new CreateUserCommandValidator().Validate(Create()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("audit.veiw")]          // a typo grants nothing at all
    [InlineData("inventory.adjust ")]   // trailing space is a different string
    [InlineData("")]
    public void Create_RefusesAnUnknownPermissionName(string role)
    {
        new CreateUserCommandValidator().Validate(Create(role)).IsValid
            .Should().BeFalse("a permission that is not in Permissions.All would silently grant nothing");
    }

    [Fact]
    public void Create_And_Update_ApplyTheSameRuleToTheSameInput()
    {
        // The create and update paths must not disagree about what a valid right is.
        foreach (var roles in new[]
        {
            new[] { Permissions.AuditView },
            new[] { "not.a.permission" },
            new[] { Permissions.Admin, "broken.value" }
        })
        {
            new CreateUserCommandValidator().Validate(Create(roles)).IsValid
                .Should().Be(new UpdateUserCommandValidator().Validate(Update(roles)).IsValid,
                    "create and update must accept and refuse exactly the same permission names");
        }
    }
}
