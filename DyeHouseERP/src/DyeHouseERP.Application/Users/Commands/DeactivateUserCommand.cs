using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Commands;

public record DeactivateUserCommand(Guid UserId) : IRequest<Unit>;

public class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public DeactivateUserCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<Unit> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // Same two safety rules as UpdateUserCommand (H1): a user cannot
        // deactivate their own login, and the last active administrator can
        // never be deactivated - either would permanently lock out a
        // single-admin installation. Kept identical in spirit and wording to
        // UpdateUserCommand so the two paths cannot drift apart.
        if (string.Equals(user.Username, _currentUser.UserName, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "You cannot deactivate your own account - ask another administrator to do it. " +
                "This rule exists so an installation with a single admin cannot lock itself out.");

        var targetIsAdmin = user.RoleList.Contains(Permissions.Admin, StringComparer.OrdinalIgnoreCase);
        if (targetIsAdmin && user.IsActive)
        {
            var otherAdmins = await _db.Users
                .Where(u => u.Id != user.Id && u.IsActive && u.Roles.Contains(Permissions.Admin))
                .AnyAsync(cancellationToken);

            if (!otherAdmins)
                throw new DomainException(
                    "This is the only active administrator. Grant the admin permission to another user first.");
        }

        user.Deactivate(_currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
