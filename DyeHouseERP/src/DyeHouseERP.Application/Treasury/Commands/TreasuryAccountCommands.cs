using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Commands;

public record CreateTreasuryAccountCommand(string Code, string Name, TreasuryAccountKind Kind) : IRequest<TreasuryAccountDto>;

public class CreateTreasuryAccountCommandValidator : AbstractValidator<CreateTreasuryAccountCommand>
{
    public CreateTreasuryAccountCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class CreateTreasuryAccountCommandHandler : IRequestHandler<CreateTreasuryAccountCommand, TreasuryAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public CreateTreasuryAccountCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    public async Task<TreasuryAccountDto> Handle(CreateTreasuryAccountCommand request, CancellationToken cancellationToken)
    {
        if (await _db.TreasuryAccounts.AnyAsync(a => a.Code == request.Code, cancellationToken))
            throw new DuplicateCodeException("Treasury account", request.Code);

        var account = new TreasuryAccount(request.Code, request.Name, request.Kind, _currentUser.UserName);
        _db.TreasuryAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);

        return new TreasuryAccountDto { Id = account.Id, Code = account.Code, Name = account.Name, Kind = account.Kind.ToString(), IsActive = account.IsActive, Balance = 0 };
    }
}
