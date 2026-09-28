using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 41: permissions are a flat, per-user list of permission names
/// (User.Roles). These tests pin the normalisation rules that User.SetProfile
/// applies, because they decide what ends up in the user's JWT claims - a
/// duplicate or padded entry would look like a different permission.
/// </summary>
public class UserPermissionTests
{
    private static User MakeUser() => new("tester", "hash", "Tester", "admin", "seeder");

    [Fact]
    public void SetProfile_TrimsDeduplicatesAndSortsRoles()
    {
        var user = MakeUser();

        user.SetProfile("مستخدم اختبار", new[] { "checks.view", "  cheques.x  ", "checks.view", "", " " }, "editor");

        user.DisplayName.Should().Be("مستخدم اختبار");
        // Sorted ordinal-ignore-case, so the order is stable across saves.
        user.RoleList.Should().Equal("checks.view", "cheques.x");
        user.Roles.Should().Be("checks.view,cheques.x");
    }

    [Fact]
    public void SetProfile_IsCaseInsensitiveWhenDeduplicating()
    {
        var user = MakeUser();

        user.SetProfile("Tester", new[] { "Checks.View", "checks.view" }, "editor");

        user.RoleList.Should().HaveCount(1);
    }

    [Fact]
    public void SetProfile_WithBlankDisplayName_Throws()
    {
        var user = MakeUser();

        var act = () => user.SetProfile("   ", new[] { "checks.view" }, "editor");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetProfile_RecordsWhoChangedIt()
    {
        var user = MakeUser();

        user.SetProfile("Tester", new[] { Permissions.ChecksExport }, "other-admin");

        user.ModifiedBy.Should().Be("other-admin");
        user.ModifiedAtUtc.Should().NotBeNull();
        user.RoleList.Should().Contain(Permissions.ChecksExport);
    }

    [Fact]
    public void SetProfile_CanClearEveryPermission()
    {
        var user = MakeUser();

        user.SetProfile("Tester", Array.Empty<string>(), "editor");

        user.RoleList.Should().BeEmpty();
        user.Roles.Should().BeEmpty();
    }

    [Fact]
    public void EveryPermissionInAll_IsDistinctAndNonEmpty()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
        Permissions.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p));
        Permissions.All.Should().Contain(Permissions.Admin);
    }
}
