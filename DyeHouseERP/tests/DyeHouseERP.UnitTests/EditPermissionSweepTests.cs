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
/// System-wide edit-permission guarantees.
///
/// The requirement is that editing, cancelling and deleting existing records is governed by
/// CONFIGURABLE, backend-enforced permissions - never by a hidden button, an admin-only role gate,
/// or a rule nobody can see. These tests sweep EVERY controller and every mutating action in the
/// API assembly and pin that:
///   1. nothing is gated on the admin ROLE (so a user holding the right permission can use it),
///   2. every mutating action declares a granular permission policy - with three documented,
///      authenticated exceptions,
///   3. every policy names a permission that actually exists in <see cref="Permissions.All"/> (a
///      typo would otherwise create a policy that no user could ever be granted), and
///   4. the real policy pipeline grants a NON-admin holding exactly that permission and refuses a
///      user who does not hold it.
/// </summary>
public class EditPermissionSweepTests
{
    /// <summary>The only mutating actions that do not carry a policy attribute, each for a stated reason.</summary>
    private static readonly Dictionary<string, string> PolicyFreeMutations = new()
    {
        ["AuthController.Login"] = "anonymous by design - it is the login endpoint",
        ["ApprovalsController.Submit"] =
            "checks the request TYPE's own permission inside SubmitApprovalRequestCommandHandler (ApprovalRequestKinds.ActionPermission)"
    };

    private static IEnumerable<Type> Controllers() =>
        typeof(UsersController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t) && t.Name.EndsWith("Controller"));

    private static readonly Type[] MutatingVerbAttributes =
    {
        typeof(HttpPostAttribute), typeof(HttpPutAttribute), typeof(HttpPatchAttribute), typeof(HttpDeleteAttribute)
    };

    private static IEnumerable<MethodInfo> MutatingActions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null)
            .Where(m => m.GetCustomAttributes().Any(a => MutatingVerbAttributes.Contains(a.GetType())));

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
        new(new ClaimsIdentity(permissions.Select(p => new Claim(ClaimTypes.Role, p)), authenticationType: "TestAuth"));

    // ------------------------------------------------------------------ structural guarantees

    [Fact]
    public void NoControllerOrAction_IsGatedOnTheAdminRole()
    {
        foreach (var controller in Controllers())
        {
            foreach (var attribute in controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                attribute.Roles.Should().BeNull(
                    $"{controller.Name} must not gate the whole controller on a role: the granular permission decides");

            foreach (var action in MutatingActions(controller))
                action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Where(a => a.Roles is not null).Should().BeEmpty(
                        $"{controller.Name}.{action.Name} must be governed by a permission, not by the admin role");
        }
    }

    [Fact]
    public void EveryMutatingAction_IsGovernedByAGranularPermissionPolicy()
    {
        var offenders = new List<string>();

        foreach (var controller in Controllers())
        {
            foreach (var action in MutatingActions(controller))
            {
                var key = $"{controller.Name}.{action.Name}";
                var policies = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Select(a => a.Policy).Where(p => p is not null).ToList();
                var allowAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                                     || controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                if (allowAnonymous || PolicyFreeMutations.ContainsKey(key)) continue;

                if (policies.Count != 1)
                    offenders.Add($"{key} declares {policies.Count} permission policies");
            }
        }

        offenders.Should().BeEmpty("every mutating action must be protected by exactly one backend permission policy");
    }

    [Fact]
    public void EveryPolicyPermission_ExistsInThePermissionList()
    {
        var known = Permissions.All.ToHashSet(StringComparer.Ordinal);
        var unknown = new List<string>();

        foreach (var controller in Controllers())
        {
            foreach (var action in MutatingActions(controller))
            {
                foreach (var policy in action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                             .Select(a => a.Policy))
                {
                    // A permission policy must be present and prefixed; an unpolicied action is
                    // covered by a different test, so a null policy is simply skipped here.
                    if (policy is null) continue;

                    policy.Should().StartWith(PermissionPolicy.Prefix);
                    var permission = policy[PermissionPolicy.Prefix.Length..];

                    if (!known.Contains(permission))
                        unknown.Add($"{controller.Name}.{action.Name} → '{permission}'");
                }
            }
        }

        unknown.Should().BeEmpty(
            "a policy naming a permission that is not in Permissions.All can never be granted to anyone");
    }

    [Fact]
    public void TheCorrectionRights_AreConfigurablePermissionsOfTheirOwn()
    {
        // Ordinary edit/create rights and the rights that reverse a posted document are deliberately
        // separate, so granting someone the ability to record a document never silently grants them the
        // authority to reverse one - and vice versa.
        var evidence = new (string Ordinary, string Correction)[]
        {
            (Permissions.RawReceive, Permissions.RawReceiptCancel),
            (Permissions.RawReturn, Permissions.RawReturnCancel),
            (Permissions.RawConsume, Permissions.ProductionIssueMaterialCancel),
            (Permissions.InventoryAdjust, Permissions.InventoryApproveNegativeStock),
            (Permissions.ReadyDeliver, Permissions.ReadyEditPostApproval),
            (Permissions.TreasuryCreate, Permissions.TreasuryCancel),
            (Permissions.PurchasesEdit, Permissions.PurchasesEditAfterReceipt)
        };

        foreach (var (ordinary, correction) in evidence)
        {
            ordinary.Should().NotBe(correction);
            Permissions.All.Should().Contain(ordinary);
            Permissions.All.Should().Contain(correction);
        }
    }

    [Fact]
    public async Task TreasuryCreate_AndTreasuryCancel_AreIndependentlyConfigurable()
    {
        // The permission split must be real at runtime: recording a movement never quietly grants the
        // right to reverse one, and the correction right never grants the ability to record. A user who
        // needs both must be granted both, explicitly.
        var service = NewAuthorizationService();

        (await service.AuthorizeAsync(Principal(Permissions.TreasuryCreate), null,
                PermissionPolicy.Prefix + Permissions.TreasuryCancel))
            .Succeeded.Should().BeFalse("treasury.create must not imply treasury.cancel at runtime");

        (await service.AuthorizeAsync(Principal(Permissions.TreasuryCancel), null,
                PermissionPolicy.Prefix + Permissions.TreasuryCreate))
            .Succeeded.Should().BeFalse("treasury.cancel must not imply treasury.create");

        (await service.AuthorizeAsync(Principal(Permissions.TreasuryCreate), null,
                PermissionPolicy.Prefix + Permissions.TreasuryCreate))
            .Succeeded.Should().BeTrue();

        (await service.AuthorizeAsync(Principal(Permissions.TreasuryCancel), null,
                PermissionPolicy.Prefix + Permissions.TreasuryCancel))
            .Succeeded.Should().BeTrue();

        // The seeded admin carries the 'admin' catch-all and therefore keeps access to both.
        (await service.AuthorizeAsync(Principal(Permissions.Admin), null,
                PermissionPolicy.Prefix + Permissions.TreasuryCancel))
            .Succeeded.Should().BeTrue("the seeded admin login must retain cancellation access after the split");
    }

    // ------------------------------------------------------------------ effective behaviour

    [Theory]
    [InlineData(Permissions.RawReceiptCancel)]
    [InlineData(Permissions.RawReturnCancel)]
    [InlineData(Permissions.ProductionIssueMaterialCancel)]
    [InlineData(Permissions.ProductionEdit)]
    [InlineData(Permissions.ItemsEdit)]
    [InlineData(Permissions.CustomersEdit)]
    [InlineData(Permissions.SuppliersEdit)]
    [InlineData(Permissions.InventoryAdjust)]
    [InlineData(Permissions.InvoicesCancel)]
    [InlineData(Permissions.PurchasesEdit)]
    [InlineData(Permissions.HrManage)]
    [InlineData(Permissions.PayrollCancel)]
    [InlineData(Permissions.TreasuryCancel)]
    [InlineData(Permissions.PurchasesEditAfterReceipt)]
    [InlineData(Permissions.InvoicesEdit)]
    [InlineData(Permissions.FormationEdit)]
    public async Task A_NonAdmin_HoldingTheEditOrCancelPermission_IsAuthorized(string permission)
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            Principal(permission), resource: null, PermissionPolicy.Prefix + permission);

        result.Succeeded.Should().BeTrue(
            $"'{permission}' is granted to the user directly, so the admin role must not be required");
    }

    [Theory]
    [InlineData(Permissions.RawReceiptCancel)]
    [InlineData(Permissions.RawReturnCancel)]
    [InlineData(Permissions.ProductionIssueMaterialCancel)]
    [InlineData(Permissions.ProductionEdit)]
    [InlineData(Permissions.ItemsEdit)]
    [InlineData(Permissions.CustomersEdit)]
    [InlineData(Permissions.InventoryAdjust)]
    [InlineData(Permissions.InvoicesCancel)]
    [InlineData(Permissions.PurchasesEdit)]
    [InlineData(Permissions.PayrollCancel)]
    [InlineData(Permissions.TreasuryCancel)]
    [InlineData(Permissions.PurchasesEditAfterReceipt)]
    [InlineData(Permissions.InvoicesEdit)]
    public async Task A_User_WithoutTheEditOrCancelPermission_IsDenied(string permission)
    {
        var service = NewAuthorizationService();

        var result = await service.AuthorizeAsync(
            Principal(Permissions.ProductionView), resource: null, PermissionPolicy.Prefix + permission);

        result.Succeeded.Should().BeFalse("holding an unrelated right never satisfies this permission");
    }

    [Fact]
    public async Task A_SignedOutUser_IsDeniedEveryPermission()
    {
        var service = NewAuthorizationService();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var permission in Permissions.All.Where(p => p != Permissions.Admin))
        {
            var result = await service.AuthorizeAsync(anonymous, resource: null, PermissionPolicy.Prefix + permission);
            result.Succeeded.Should().BeFalse($"'{permission}' must not be reachable without signing in");
        }
    }
}
