using System.Reflection;
using System.Security.Claims;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// D2: the effective GRANULAR permission controls access. The three administration controllers
/// (users, audit log, period close) used to carry a class-level [Authorize(Roles = "admin")] in
/// ADDITION to their per-action policies. ASP.NET Core ANDs stacked [Authorize] attributes, so a
/// non-admin user who had been granted e.g. audit.view was still refused - the permission was
/// grantable in the UI but impossible to use.
///
/// These tests pin both halves:
///   1. the controllers no longer carry a role gate (and still require authentication), and
///   2. the real policy pipeline (provider + handler + IAuthorizationService) grants a NON-admin
///      principal that holds the exact permission, refuses one that does not, and still lets the
///      admin catch-all through.
/// </summary>
public class AuthorizationPolicyTests
{
    private static readonly Type[] AdministrationControllers =
    {
        typeof(UsersController), typeof(AuditLogController), typeof(PeriodClosingController)
    };

    private static IAuthorizationService NewAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(params string[] permissions) =>
        new(new ClaimsIdentity(
            permissions.Select(p => new Claim(ClaimTypes.Role, p)), authenticationType: "TestAuth"));

    // ------------------------------------------------------------------ structural guarantees

    [Fact]
    public void AdministrationControllers_NoLongerCarryAClassLevelRoleGate()
    {
        foreach (var controller in AdministrationControllers)
        {
            var classAttributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

            classAttributes.Where(a => a.Roles is not null).Should().BeEmpty(
                $"{controller.Name} must not gate the whole controller on a role: the granular permission decides");
        }
    }

    [Fact]
    public void AdministrationControllers_StillRequireAuthenticationOnEveryAction()
    {
        foreach (var controller in AdministrationControllers)
        {
            // Class level: a bare [Authorize] keeps authentication mandatory for any future action
            // that forgets its own attribute, without restricting it to a role.
            controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Count(a => a.Policy is null && a.Roles is null)
                .Should().Be(1,
                    $"{controller.Name} must keep a bare [Authorize] as its authentication floor");

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                                                        BindingFlags.DeclaredOnly))
            {
                if (action.GetCustomAttribute<NonActionAttribute>() is not null) continue;

                var declaresOwn = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                                  || controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                declaresOwn.Should().BeTrue(
                    $"{controller.Name}.{action.Name} must not be reachable without authorization");
            }
        }
    }

    [Fact]
    public void EveryAdministrationAction_CarriesAGranularPermissionPolicy()
    {
        // The actions are exactly the ones whose access the decision moved from "admin role"
        // to the granular right, so each must name its permission explicitly.
        var expected = new (Type Controller, string Action, string Permission)[]
        {
            (typeof(UsersController), nameof(UsersController.Get), Permissions.UsersManage),
            (typeof(UsersController), nameof(UsersController.Create), Permissions.UsersManage),
            (typeof(UsersController), nameof(UsersController.Update), Permissions.RolesManage),
            (typeof(UsersController), nameof(UsersController.Deactivate), Permissions.UsersManage),
            (typeof(UsersController), nameof(UsersController.ChangePassword), Permissions.UsersManage),
            (typeof(AuditLogController), nameof(AuditLogController.Get), Permissions.AuditView),
            (typeof(PeriodClosingController), nameof(PeriodClosingController.Get), Permissions.SettingsManage),
            (typeof(PeriodClosingController), nameof(PeriodClosingController.Close), Permissions.SettingsManage),
            (typeof(PeriodClosingController), nameof(PeriodClosingController.Reopen), Permissions.SettingsManage)
        };

        foreach (var (controller, actionName, permission) in expected)
        {
            var action = controller.GetMethod(actionName)
                ?? throw new InvalidOperationException($"{controller.Name}.{actionName} not found");
            var policy = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Select(a => a.Policy).SingleOrDefault(p => p is not null);

            policy.Should().Be(PermissionPolicy.Prefix + permission);
        }
    }

    // ------------------------------------------------------------------ effective behaviour

    [Theory]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.RolesManage)]
    [InlineData(Permissions.AuditView)]
    [InlineData(Permissions.SettingsManage)]
    public async Task ANonAdminHoldingThePermission_IsAuthorized(string permission)
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            Principal(permission), resource: null, PermissionPolicy.Prefix + permission);

        result.Succeeded.Should().BeTrue(
            $"'{permission}' is the permission the action declares, so holding it must be enough (no admin role required)");
    }

    [Theory]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.RolesManage)]
    [InlineData(Permissions.AuditView)]
    [InlineData(Permissions.SettingsManage)]
    public async Task AUserWithoutThePermission_IsRejected(string permission)
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            Principal(Permissions.ProductionView), resource: null, PermissionPolicy.Prefix + permission);

        result.Succeeded.Should().BeFalse("a different permission never satisfies this one");
    }

    [Fact]
    public async Task ASignedOutPrincipal_IsRejected()
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()), resource: null,
            PermissionPolicy.Prefix + Permissions.AuditView);

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.AuditView)]
    [InlineData(Permissions.SettingsManage)]
    public async Task TheAdminCatchAll_StillPassesWithoutTheSpecificPermission(string permission)
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            Principal(Permissions.Admin), resource: null, PermissionPolicy.Prefix + permission);

        result.Succeeded.Should().BeTrue("'admin' remains the documented catch-all");
    }
}
