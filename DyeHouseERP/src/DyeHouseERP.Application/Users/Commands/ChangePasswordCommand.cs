using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Users.Commands;

public record ChangePasswordCommand(Guid UserId, string NewPassword) : IRequest<Unit>;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
        // R5: same ceiling as account creation - 8..200 characters.
        => RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(200);
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher)
    {
        _db = db; _currentUser = currentUser; _passwordHasher = passwordHasher;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // Server-side: resetting a password is modifying the account, so an administrator account
        // can only be reset by an administrator - otherwise users.manage would be a route to
        // taking over an admin login instead of a way to help an ordinary user back in.
        UserAdministrationGuard.EnsureCanModifyAccount(_currentUser, user);

        user.ChangePassword(_passwordHasher.Hash(request.NewPassword), _currentUser.UserName);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
