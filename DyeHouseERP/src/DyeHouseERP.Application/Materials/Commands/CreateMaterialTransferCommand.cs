using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Materials.Commands;

/// <summary>Moves a material between two material warehouses (spec section 26).</summary>
public record CreateMaterialTransferCommand(Guid MaterialId, Guid FromWarehouseId, Guid ToWarehouseId, decimal Quantity, string? Notes)
    : IRequest<MaterialTransferDto>;

public class CreateMaterialTransferCommandValidator : AbstractValidator<CreateMaterialTransferCommand>
{
    public CreateMaterialTransferCommandValidator()
    {
        RuleFor(x => x.MaterialId).NotEmpty();
        RuleFor(x => x.ToWarehouseId).NotEqual(x => x.FromWarehouseId);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class CreateMaterialTransferCommandHandler : IRequestHandler<CreateMaterialTransferCommand, MaterialTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IMaterialLedgerService _ledger;
    private readonly IDateTime _clock;

    public CreateMaterialTransferCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IMaterialLedgerService ledger, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _ledger = ledger; _clock = clock;
    }

    public async Task<MaterialTransferDto> Handle(CreateMaterialTransferCommand request, CancellationToken cancellationToken)
    {
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.MaterialId);

        var balance = await _ledger.GetBalanceAsync(request.MaterialId, request.FromWarehouseId, cancellationToken);
        if (request.Quantity > balance)
            throw new NegativeStockException(balance, request.Quantity);

        var transferNumber = await _numberGenerator.NextAsync(DocumentType.MaterialTransfer, cancellationToken: cancellationToken);

        var transfer = new MaterialTransfer(transferNumber, _clock.UtcNow, request.MaterialId,
            request.FromWarehouseId, request.ToWarehouseId, request.Quantity, _currentUser.UserName, request.Notes);
        _db.MaterialTransfers.Add(transfer);

        _db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.MaterialTransfer, transfer.TransferNumber, transfer.Id, _clock.UtcNow,
            request.MaterialId, request.FromWarehouseId, null, request.Quantity, MaterialTransactionDirection.Out, null, _currentUser.UserName));
        _db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.MaterialTransfer, transfer.TransferNumber, transfer.Id, _clock.UtcNow,
            request.MaterialId, request.ToWarehouseId, null, request.Quantity, MaterialTransactionDirection.In, null, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        var fromWh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.FromWarehouseId, cancellationToken);
        var toWh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == request.ToWarehouseId, cancellationToken);

        return new MaterialTransferDto
        {
            Id = transfer.Id, TransferNumber = transfer.TransferNumber, TransferDate = transfer.TransferDate,
            MaterialId = material.Id, MaterialCode = material.Code,
            FromWarehouseId = request.FromWarehouseId, FromWarehouseName = fromWh?.Name ?? "",
            ToWarehouseId = request.ToWarehouseId, ToWarehouseName = toWh?.Name ?? "",
            Quantity = transfer.Quantity, Notes = transfer.Notes
        };
    }
}
