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
    public const string Prefix = "Permission:";
    public static string For(string permission) => Prefix + permission;
}

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
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
    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

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
