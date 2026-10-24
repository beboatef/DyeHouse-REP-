using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Separates.Commands;

/// <summary>
/// Creates a brand-new Production Order to reprocess a Separate (spec
/// section 23). The original order's history is NEVER edited or
/// overwritten - genealogy is preserved purely through
/// ProductionOrder.ReprocessingOfProductionOrderId pointing back to it.
///
/// <paramref name="StartingStageId"/> is optional and must name an EXISTING, ACTIVE stage:
/// reprocessed material is already past formation (it came out of a later operation), so the
/// job may legitimately start where the rework actually begins. Empty means the ordinary
/// route start (التشكيل). Either way the order starts at exactly ONE stage and the rest of the
/// route stays dynamic, and no inventory movement is posted by this command - the raw issue
/// remains the single deduction.
/// </summary>
public record ReprocessSeparateCommand(
    Guid SeparateId, DateTime OrderDate, string? Notes, Guid? StartingStageId = null) : IRequest<ProductionOrderDto>;

public class ReprocessSeparateCommandValidator : AbstractValidator<ReprocessSeparateCommand>
{
    public ReprocessSeparateCommandValidator()
    {
        RuleFor(x => x.SeparateId).NotEmpty();
    }
}

public class ReprocessSeparateCommandHandler : IRequestHandler<ReprocessSeparateCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public ReprocessSeparateCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
        _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<ProductionOrderDto> Handle(ReprocessSeparateCommand request, CancellationToken cancellationToken)
    {
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var separate = await _db.Separates.FirstOrDefaultAsync(s => s.Id == request.SeparateId, cancellationToken)
            ?? throw new NotFoundException("Separate", request.SeparateId);

        if (separate.Status != SeparateStatus.PendingReprocessing)
            throw new DomainException(
                $"Separate is {separate.Status} and cannot start another reprocessing order.");

        // Spec sections 5-6: the reprocessing order's requested quantity may only use the
        // item's configured units, and both when the item requires both.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, separate.ItemId, separate.QuantityKg, separate.QuantityMeter,
            cancellationToken, "on this reprocessing order");

        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Kind == WarehouseKind.ReadyGoods)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, readyWarehouse.Id, cancellationToken, "to issue separates for reprocessing");
        if (readyWarehouse.Kind != WarehouseKind.ReadyGoods)
            throw new DomainException("Separates can only be issued from the Ready Goods warehouse.");

        // The Ready Goods ledger is authoritative. Lock the original order's separates lot,
        // then validate its remaining balance before creating the new order or writing any row.
        await using (await _stockLock.AcquireAsync(
            StockLockKey.ReadyLot(separate.ItemId, separate.OriginalProductionOrderId), cancellationToken))
        {
            var balanceRows = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == readyWarehouse.Id
                         && t.RawMessageId == null
                         && t.ProductionOrderId == separate.OriginalProductionOrderId
                         && t.CustomerId == separate.CustomerId
                         && t.ItemId == separate.ItemId
                         && t.ReadyGoodsCategory == ReadyGoodsCategory.Separates)
                .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                .ToListAsync(cancellationToken);

            var availableKg = balanceRows.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);
            var availableMeter = balanceRows.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction);

            if (separate.QuantityKg is > 0 && separate.QuantityKg > availableKg)
                throw new NegativeStockException(availableKg, separate.QuantityKg.Value);
            if (separate.QuantityMeter is > 0 && separate.QuantityMeter > availableMeter)
                throw new NegativeStockException(availableMeter, separate.QuantityMeter.Value);

            var orderNumber = await _numberGenerator.NextAsync(
                DocumentType.ProductionOrder, cancellationToken: cancellationToken);

            var newOrder = new ProductionOrder(
                orderNumber, separate.CustomerId, separate.ItemId, request.OrderDate, _currentUser.UserName,
                requestedQuantityKg: separate.QuantityKg, requestedQuantityMeter: separate.QuantityMeter,
                notes: request.Notes, reprocessingOfProductionOrderId: separate.OriginalProductionOrderId);

            // Only ACTIVE stages are offered, so a stage that does not exist - or that has been
            // deactivated - can never become the starting stage (the domain lookup refuses it).
            var activeStages = await _db.ProductionStageDefinitions.AsNoTracking()
                .Where(s => s.IsActive).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
            newOrder.BuildStageRoute(activeStages, _currentUser.UserName, request.StartingStageId);

            _db.ProductionOrders.Add(newOrder);
            separate.StartReprocessing(newOrder.Id);

            // This OUT movement consumes the separate from its original Ready Goods category.
            // The new order is the source document; ProductionOrderId remains the ORIGINAL order
            // so the remaining separates balance is still traced to the lot that created it.
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, newOrder.OrderNumber, newOrder.Id, postingDate,
                readyWarehouse.Id, separate.CustomerId, separate.ItemId,
                rawMessageId: null, productionOrderId: separate.OriginalProductionOrderId,
                quantityKg: separate.QuantityKg, quantityMeter: separate.QuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName,
                readyGoodsCategory: ReadyGoodsCategory.Separates));

            await _db.SaveChangesAsync(cancellationToken);
            return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, newOrder.Id, cancellationToken);
        }
    }
}
