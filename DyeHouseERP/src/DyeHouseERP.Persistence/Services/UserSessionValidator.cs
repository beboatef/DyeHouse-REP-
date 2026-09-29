using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence.Services;

/// <summary>
/// H5: resolves the user's CURRENT row while a bearer token is being validated.
///
/// Implemented as a single untracked, projected primary-key lookup - no lazy
/// loading, no change tracking, no allocation of the entity - so adding it to
/// every authenticated request costs one narrow index seek rather than a
/// materializing query.
///
/// It is deliberately a separate service from ICurrentUserService (which trusts
/// the token's claims): this one is the authority on whether those claims are
/// still true.
/// </summary>
public class UserSessionValidator : IUserSessionValidator
{
    private readonly ApplicationDbContext _context;

    public UserSessionValidator(ApplicationDbContext context) => _context = context;

    public async Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.IsActive, u.Roles })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        return new UserSessionState(
            userId,
            row.Username,
            row.IsActive,
            row.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
