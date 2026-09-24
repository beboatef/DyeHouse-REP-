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

/// <summary>"الإحلال" - preparing/diluting a material with water/solvent (spec section 28). Consumes the original material's stock; the resulting solution itself is not tracked as separate inventory.</summary>
public record CreateMaterialPreparationCommand(
    Guid OriginalMaterialId, Guid WarehouseId, decimal OriginalQuantity, decimal WaterQuantity, decimal ResultingQuantity,
    decimal? Concentration, Guid? ProductionOrderId, string? Notes) : IRequest<MaterialPreparationDto>;

public class CreateMaterialPreparationCommandValidator : AbstractValidator<CreateMaterialPreparationCommand>
{
    public CreateMaterialPreparationCommandValidator()
    {
        RuleFor(x => x.OriginalMaterialId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.OriginalQuantity).GreaterThan(0);
        RuleFor(x => x.ResultingQuantity).GreaterThan(0);
    }
}

public class CreateMaterialPreparationCommandHandler : IRequestHandler<CreateMaterialPreparationCommand, MaterialPreparationDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IMaterialLedgerService _ledger;
    private readonly IDateTime _clock;

    public CreateMaterialPreparationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IMaterialLedgerService ledger, IDateTime clock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _ledger = ledger; _clock = clock;
    }

    public async Task<MaterialPreparationDto> Handle(CreateMaterialPreparationCommand request, CancellationToken cancellationToken)
    {
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == request.OriginalMaterialId, cancellationToken)
            ?? throw new NotFoundException("Material", request.OriginalMaterialId);

        var balance = await _ledger.GetBalanceAsync(request.OriginalMaterialId, request.WarehouseId, cancellationToken);
        if (request.OriginalQuantity > balance)
            throw new NegativeStockException(balance, request.OriginalQuantity);

        var cost = material.PurchasePrice * request.OriginalQuantity;
        var preparationNumber = await _numberGenerator.NextAsync(DocumentType.PreparationDilution, cancellationToken: cancellationToken);

        var preparation = new MaterialPreparation(
            preparationNumber, _clock.UtcNow, request.OriginalMaterialId, request.WarehouseId,
            request.OriginalQuantity, request.WaterQuantity, request.ResultingQuantity, _currentUser.UserName,
            request.Concentration, cost, request.ProductionOrderId, request.Notes);
        _db.MaterialPreparations.Add(preparation);

        _db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PreparationDilution, preparation.PreparationNumber, preparation.Id, _clock.UtcNow,
            request.OriginalMaterialId, request.WarehouseId, request.ProductionOrderId,
            request.OriginalQuantity, MaterialTransactionDirection.Out, material.PurchasePrice, _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        return new MaterialPreparationDto
        {
            Id = preparation.Id, PreparationNumber = preparation.PreparationNumber, PreparationDate = preparation.PreparationDate,
            OriginalMaterialId = material.Id, OriginalMaterialCode = material.Code,
            OriginalQuantity = preparation.OriginalQuantity, WaterQuantity = preparation.WaterQuantity,
            ResultingQuantity = preparation.ResultingQuantity, Concentration = preparation.Concentration,
            Cost = preparation.Cost, ProductionOrderId = preparation.ProductionOrderId, Notes = preparation.Notes
        };
    }
}
