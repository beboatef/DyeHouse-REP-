using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Users.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Finding 1 (permission escalation). The model is a flat per-user list of permission names
/// (User.Roles) checked as role claims, with "admin" as the catch-all permission that passes
/// every check. The endpoints are protected by granular permissions - users.manage for the
/// account itself, roles.manage for the permission set - but nothing in the HANDLERS verified
/// that the caller actually held the rights they were handing out. A non-admin holding
/// users.manage/roles.manage could therefore mint an "admin" account, or grant itself/others
/// permissions it never had.
///
/// These tests pin the server-side rules:
///   1. you may only grant permissions you hold yourself;
///   2. only an administrator may grant or revoke "admin";
///   3. administrator accounts may only be modified by administrators;
///   4. an authorized admin can still do ordinary user administration;
///   5. the last-active-admin protections are unchanged.
///
/// The "actor" mock mirrors Infrastructure.Services.CurrentUserService: HasPermission(p) is
/// true when p is held or when the actor holds the "admin" catch-all.
/// </summary>
public class UserPermissionEscalationTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public UserPermissionEscalationTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"user-escalation-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private static ICurrentUserService Actor(string username, params string[] permissions)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.UserName).Returns(username);
        mock.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        mock.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns((string role) => permissions.Contains(role, StringComparer.OrdinalIgnoreCase));
        mock.Setup(u => u.HasPermission(It.IsAny<string>()))
            .Returns((string permission) =>
                permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)
                || permissions.Contains(Permissions.Admin, StringComparer.OrdinalIgnoreCase));
        return mock.Object;
    }

    private Guid SeedUser(string username, string displayName, params string[] roles)
    {
        var user = new User(username, "hash", displayName, string.Join(",", roles), "seed");
        using var db = NewDb();
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private UpdateUserCommandHandler Update(Persistence.ApplicationDbContext db, ICurrentUserService actor) => new(db, actor);

    private static CreateUserCommandHandler Create(Persistence.ApplicationDbContext db, ICurrentUserService actor) =>
        new(db, actor, new PasswordHasher());

    // ------------------------------------------------------- rule 1 + 2 (grant what you hold)

    [Fact]
    public async Task NonAdmin_WithRolesManage_CannotGrantAdmin_OnCreate()
    {
        using var db = NewDb();

        var act = async () => await Create(db, Actor("delegated", Permissions.UsersManage))
            .Handle(new CreateUserCommand("newboss", "Password123", "New Boss", new List<string> { Permissions.Admin }),
                CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.Should().NotContain(u => u.Username == "newboss");
    }

    [Fact]
    public async Task NonAdmin_WithRolesManage_CannotGrantAdmin_OnUpdate()
    {
        var targetId = SeedUser("clerk", "Clerk", Permissions.InventoryView);
        using var db = NewDb();

        var act = async () => await Update(db, Actor("delegated", Permissions.RolesManage))
            .Handle(new UpdateUserCommand(targetId, "Clerk", new List<string> { Permissions.Admin }), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.First(u => u.Id == targetId).RoleList.Should().NotContain(Permissions.Admin);
    }

    [Fact]
    public async Task User_CannotGrantAPermissionTheyDoNotHold_OnCreate()
    {
        using var db = NewDb();

        var act = async () => await Create(db, Actor("delegated", Permissions.UsersManage))
            .Handle(new CreateUserCommand("biller", "Password123", "Biller",
                new List<string> { Permissions.InvoicesIssue }), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.Should().NotContain(u => u.Username == "biller");
    }

    [Fact]
    public async Task User_CannotGrantAPermissionTheyDoNotHold_OnUpdate()
    {
        var targetId = SeedUser("clerk", "Clerk");
        using var db = NewDb();

        var act = async () => await Update(db, Actor("delegated", Permissions.RolesManage))
            .Handle(new UpdateUserCommand(targetId, "Clerk", new List<string> { Permissions.InvoicesIssue }),
                CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.First(u => u.Id == targetId).RoleList.Should().BeEmpty();
    }

    // ------------------------------------------------------- rule 3 (admin accounts)

    [Fact]
    public async Task NonAdmin_CannotModifyAnAdminAccount()
    {
        var adminId = SeedUser("real-admin", "Real Admin", Permissions.Admin, Permissions.UsersManage);
        SeedUser("second-admin", "Second Admin", Permissions.Admin, Permissions.UsersManage);
        using var db = NewDb();

        // Not the last admin (a second active admin exists), so only the admin-account rule can stop this.
        var act = async () => await Update(db, Actor("delegated", Permissions.RolesManage))
            .Handle(new UpdateUserCommand(adminId, "Renamed", new List<string> { Permissions.UsersManage }),
                CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        var admin = verify.Users.First(u => u.Id == adminId);
        admin.RoleList.Should().Contain(Permissions.Admin);
        admin.DisplayName.Should().Be("Real Admin");
    }

    [Fact]
    public async Task NonAdmin_CannotDeactivateAnAdminAccount()
    {
        var adminId = SeedUser("real-admin", "Real Admin", Permissions.Admin, Permissions.UsersManage);
        SeedUser("second-admin", "Second Admin", Permissions.Admin, Permissions.UsersManage);
        using var db = NewDb();

        var act = async () => await new DeactivateUserCommandHandler(db, Actor("delegated", Permissions.UsersManage))
            .Handle(new DeactivateUserCommand(adminId), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.First(u => u.Id == adminId).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task NonAdmin_CannotChangeAnAdminPassword()
    {
        SeedUser("real-admin", "Real Admin", Permissions.Admin, Permissions.UsersManage);
        var adminId = SeedUser("real-admin-2", "Real Admin 2", Permissions.Admin, Permissions.UsersManage);
        using var db = NewDb();

        var act = async () => await new ChangePasswordCommandHandler(
                db, Actor("delegated", Permissions.UsersManage), new PasswordHasher())
            .Handle(new ChangePasswordCommand(adminId, "Hijacked123"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        using var verify = NewDb();
        verify.Users.First(u => u.Id == adminId).PasswordHash.Should().Be("hash");
    }

    // ------------------------------------------------------- rule 4 (still works)

    [Fact]
    public async Task Admin_CanGrantPermissionsWithinTheirAuthority_AndIsRecorded()
    {
        var targetId = SeedUser("clerk", "Clerk");
        using var db = NewDb();

        var result = await Update(db, Actor("real-admin", Permissions.Admin))
            .Handle(new UpdateUserCommand(targetId, "Clerk Two",
                new List<string> { Permissions.InvoicesIssue, Permissions.InventoryView }), CancellationToken.None);

        result.DisplayName.Should().Be("Clerk Two");
        result.Roles.Should().BeEquivalentTo(new[] { Permissions.InvoicesIssue, Permissions.InventoryView });

        using var verify = NewDb();
        var saved = verify.Users.First(u => u.Id == targetId);
        saved.RoleList.Should().BeEquivalentTo(new[] { Permissions.InvoicesIssue, Permissions.InventoryView });
        saved.ModifiedBy.Should().Be("real-admin");
    }

    [Fact]
    public async Task DelegatedRoleAdministrator_CanGrantPermissionsTheyThemselvesHold()
    {
        var targetId = SeedUser("clerk", "Clerk");
        using var db = NewDb();

        var result = await Update(db, Actor("delegated", Permissions.RolesManage, Permissions.ReportsView))
            .Handle(new UpdateUserCommand(targetId, "Clerk", new List<string> { Permissions.ReportsView }),
                CancellationToken.None);

        result.Roles.Should().BeEquivalentTo(new[] { Permissions.ReportsView });
    }

    [Fact]
    public async Task Admin_CanDeactivateAndChangeThePasswordOfAnOrdinaryUser()
    {
        var targetId = SeedUser("clerk", "Clerk");

        using (var db = NewDb())
        {
            await new ChangePasswordCommandHandler(db, Actor("real-admin", Permissions.Admin), new PasswordHasher())
                .Handle(new ChangePasswordCommand(targetId, "NewPassword123"), CancellationToken.None);
        }

        using (var db = NewDb())
        {
            await new DeactivateUserCommandHandler(db, Actor("real-admin", Permissions.Admin))
                .Handle(new DeactivateUserCommand(targetId), CancellationToken.None);
        }

        using var verify = NewDb();
        var user = verify.Users.First(u => u.Id == targetId);
        user.PasswordHash.Should().NotBe("hash");
        user.IsActive.Should().BeFalse();
    }

    // ------------------------------------------------------- rule 5 (last-admin protection)

    [Fact]
    public async Task LastActiveAdmin_StillCannotLoseTheAdminPermission()
    {
        var onlyAdminId = SeedUser("only-admin", "Only Admin", Permissions.Admin, Permissions.UsersManage);
        using var db = NewDb();

        // The actor may not touch an admin account at all, and the installation must keep an admin.
        var act = async () => await Update(db, Actor("delegated", Permissions.RolesManage))
            .Handle(new UpdateUserCommand(onlyAdminId, "Only Admin", new List<string>()), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();

        using var verify = NewDb();
        verify.Users.First(u => u.Id == onlyAdminId).RoleList.Should().Contain(Permissions.Admin);
    }

    [Fact]
    public async Task LastActiveAdmin_StillCannotBeDemoted_ByAnotherAdministrator()
    {
        var onlyAdminId = SeedUser("only-admin", "Only Admin", Permissions.Admin, Permissions.UsersManage);
        // The acting administrator's own stored account is inactive, so it does not count as
        // a second active admin - exactly the state the last-admin rule protects.
        var actorId = SeedUser("retired-admin", "Retired Admin", Permissions.Admin, Permissions.UsersManage);
        using (var db = NewDb())
        {
            await new DeactivateUserCommandHandler(db, Actor("real-admin", Permissions.Admin))
                .Handle(new DeactivateUserCommand(actorId), CancellationToken.None);
        }

        using var db2 = NewDb();
        var act = async () => await Update(db2, Actor("retired-admin", Permissions.Admin))
            .Handle(new UpdateUserCommand(onlyAdminId, "Only Admin", new List<string> { Permissions.UsersManage }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("only active administrator");

        using var verify = NewDb();
        verify.Users.First(u => u.Id == onlyAdminId).RoleList.Should().Contain(Permissions.Admin);
    }
}
