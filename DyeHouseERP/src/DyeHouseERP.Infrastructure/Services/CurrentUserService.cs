using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace DyeHouseERP.Infrastructure.Services;

/// <summary>
/// Reads the authenticated identity out of the current HTTP request's JWT
/// claims. Falls back to "system" outside an HTTP context (background jobs).
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string UserName =>
        _httpContextAccessor.HttpContext?.User?.Identity?.Name
        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("preferred_username")?.Value
        ?? "system";

    public Guid? UserId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsInRole(string role) => _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;

    /// <summary>
    /// Permissions are carried as the same role claims IsInRole checks (see
    /// JwtTokenService - each entry in User.Roles becomes a ClaimTypes.Role
    /// claim), so an "admin" user's catch-all role plus any specific
    /// permission strings (e.g. "raw.consume") both work through this one
    /// check. This is the "role-string checks" tradeoff called out in the
    /// README as a starting point, not a full claims/policy engine.
    /// </summary>
    public bool HasPermission(string permission) => IsInRole(permission) || IsInRole("admin");

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
}
