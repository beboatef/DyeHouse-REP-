using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Treasury.Commands;

public record CreateTreasuryTransferCommand(DateTime TransferDate, Guid FromAccountId, Guid ToAccountId, decimal Amount, string? Description)
    : IRequest<TreasuryTransferDto>;

public class CreateTreasuryTransferCommandValidator : AbstractValidator<CreateTreasuryTransferCommand>
{
    public CreateTreasuryTransferCommandValidator()
    {
        RuleFor(x => x.ToAccountId).NotEqual(x => x.FromAccountId);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public class CreateTreasuryTransferCommandHandler : IRequestHandler<CreateTreasuryTransferCommand, TreasuryTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;

    public CreateTreasuryTransferCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
    }

    public async Task<TreasuryTransferDto> Handle(CreateTreasuryTransferCommand request, CancellationToken cancellationToken)
    {
        var fromAccount = await _db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.FromAccountId);
        var toAccount = await _db.TreasuryAccounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId, cancellationToken)
            ?? throw new NotFoundException("TreasuryAccount", request.ToAccountId);

        var transferNumber = await _numberGenerator.NextAsync(DocumentType.TreasuryTransfer, cancellationToken: cancellationToken);

        var transfer = new TreasuryTransfer(transferNumber, request.TransferDate, request.FromAccountId, request.ToAccountId,
            request.Amount, _currentUser.UserName, request.Description);
        _db.TreasuryTransfers.Add(transfer);

        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.FromAccountId, request.TransferDate, DocumentType.TreasuryTransfer, transferNumber, transfer.Id,
            request.Amount, TreasuryDirection.Out, request.Description, _currentUser.UserName));
        _db.TreasuryTransactions.Add(new TreasuryTransaction(
            request.ToAccountId, request.TransferDate, DocumentType.TreasuryTransfer, transferNumber, transfer.Id,
            request.Amount, TreasuryDirection.In, request.Description, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new TreasuryTransferDto
        {
            Id = transfer.Id, TransferNumber = transfer.TransferNumber, TransferDate = transfer.TransferDate,
            FromAccountId = fromAccount.Id, FromAccountName = fromAccount.Name,
            ToAccountId = toAccount.Id, ToAccountName = toAccount.Name,
            Amount = transfer.Amount, Description = transfer.Description
        };
    }
}
