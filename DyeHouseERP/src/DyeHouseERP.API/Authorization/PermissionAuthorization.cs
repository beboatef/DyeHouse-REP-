using DyeHouseERP.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace DyeHouseERP.API.Authorization;

/// <summary>
/// Marks an endpoint as requiring one specific permission string from
/// Domain.Common.Permissions (spec section 41). Usage:
///   [Authorize(Policy = PermissionPolicy.For(Permissions.CustomersCreate))]
/// The policy name is resolved dynamically by PermissionPolicyProvider so a
/// new permission never needs a matching services.AddAuthorization(...)
/// registration - the string IS the policy.
/// </summary>
public static class PermissionPolicy
{
    /// <summary>The policy-name prefix that marks a policy as a permission policy.</summary>
    public const string Prefix = "Permission:";
    /// <summary>Builds the authorization policy name for one permission string.</summary>
    public static string For(string permission) => Prefix + permission;
}

/// <summary>Requires the signed-in user to hold one specific permission (or the admin catch-all).</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>The permission string the user must hold.</summary>
    public string Permission { get; }
    /// <summary>Initializes a new instance of the PermissionRequirement class.</summary>
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>
/// Grants access if the user holds the specific permission's role claim, OR
/// the catch-all "admin" role (spec: admin manages everything without every
/// permission having to be assigned one by one). Denies otherwise - server-side,
/// never inferred from what the frontend chose to hide (spec section 41).
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>Succeeds when the user holds the permission claim or the admin catch-all; otherwise does nothing (deny).</summary>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.IsInRole(requirement.Permission) || context.User.IsInRole(Permissions.Admin))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>Turns any "Permission:xxx" policy name into a PermissionRequirement on the fly, so permissions never need individual AddAuthorization registrations.</summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;
    /// <summary>Initializes a new instance of the PermissionPolicyProvider class.</summary>
    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    /// <summary>Returns the default authorization policy.</summary>
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    /// <summary>Returns the fallback authorization policy.</summary>
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <summary>Turns a "Permission:xxx" policy name into a PermissionRequirement; delegates anything else.</summary>
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[PermissionPolicy.Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
