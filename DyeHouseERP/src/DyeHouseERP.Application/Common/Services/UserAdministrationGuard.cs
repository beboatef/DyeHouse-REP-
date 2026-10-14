using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;

namespace DyeHouseERP.Application.Common.Services;

/// <summary>
/// The rules that stop an account with user-administration rights from turning those rights into
/// rights it was never given (spec section 41). The endpoints are gated by granular permissions
/// (users.manage for the account, roles.manage for the permission set) and the UI hides what it
/// will not send - but neither is the enforcement point, so every write path that can hand out
/// rights or touch an administrator account is checked HERE, server-side, against the
/// AUTHENTICATED caller's own claims:
///
///   1. you may only grant permissions you hold yourself - so a delegated role administrator
///      (roles.manage without admin) cannot mint rights they do not have;
///   2. "admin" is the catch-all permission that passes every check, so it can never be implied
///      by roles.manage: only an administrator may grant or revoke it;
///   3. an administrator account may only be modified (edited, deactivated, password changed) by
///      an administrator - otherwise users.manage would be a route to taking one over.
///
/// The existing last-active-admin and self-lockout protections stay where they are, unchanged.
/// </summary>
public static class UserAdministrationGuard
{
    /// <summary>
    /// Rejects the request unless the caller already holds every permission they are handing out.
    /// Only the permissions PRESENT in <paramref name="requestedPermissions"/> are judged: dropping
    /// a right is not granting one, and the administrator-account rule above covers revocation on
    /// an admin account.
    /// </summary>
    public static void EnsureCanGrantPermissions(ICurrentUserService actor, IEnumerable<string> requestedPermissions)
    {
        foreach (var permission in requestedPermissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsAdminPermission(permission))
            {
                if (!HoldsAdmin(actor))
                    throw new UnauthorizedAccessException(
                        $"Only an administrator can grant the '{Permissions.Admin}' permission.");
                continue;
            }

            if (!actor.HasPermission(permission))
                throw new UnauthorizedAccessException(
                    $"You cannot grant the '{permission}' permission because your own account does not hold it.");
        }
    }

    /// <summary>Rejects the request when the target holds the admin catch-all and the caller does not.</summary>
    public static void EnsureCanModifyAccount(ICurrentUserService actor, User target)
    {
        if (HoldsAdminPermission(target.RoleList) && !HoldsAdmin(actor))
            throw new UnauthorizedAccessException(
                "Only an administrator can modify an administrator account.");
    }

    /// <summary>True when the given permission list contains the admin catch-all.</summary>
    public static bool HoldsAdminPermission(IEnumerable<string> permissions) =>
        permissions.Contains(Permissions.Admin, StringComparer.OrdinalIgnoreCase);

    private static bool IsAdminPermission(string permission) =>
        string.Equals(permission, Permissions.Admin, StringComparison.OrdinalIgnoreCase);

    /// <summary>The caller is an administrator: they hold the admin catch-all themselves.</summary>
    private static bool HoldsAdmin(ICurrentUserService actor) => actor.HasPermission(Permissions.Admin);
}
