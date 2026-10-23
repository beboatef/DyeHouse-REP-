using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ReadyGoods.Commands;

/// <summary>
/// Moves ready (finished) stock BACK into raw-material stock - for rework or reclassification
/// (spec section 29).
///
/// This is the ONLY way stock may leave Ready Goods for the raw side, and it exists precisely because
/// nothing in the system may do it implicitly: undelivered goods stay in Ready Goods, and no delivery,
/// cancellation, order completion or reprocessing turns them back into raw material on its own. The
/// move is therefore its own document - authorized by its own permission
/// (<see cref="DyeHouseERP.Domain.Common.Permissions.ReadyReturnToRaw"/>), carrying a MANDATORY reason,
/// traceable to the order it came from and the raw lot it lands on, and audited.
///
/// It posts two ledger rows in one transaction: an OUT against the order's ready lot (the same
/// (item, production order) dimension a ready-goods transfer posts IN against and a delivery posts OUT
/// against) and an IN against the destination raw lot. The original rows are never edited, and the
/// ready balance is validated under the stock lock before anything is written, so the move can never
/// overdraw the ready lot even under concurrent requests.
/// </summary>
public record ReturnReadyGoodsToRawStockCommand(
    Guid ProductionOrderId,
    Guid ItemId,
    Guid RawMessageId,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    string Reason,
    string? Notes = null,
    ReadyGoodsCategory? Category = null) : IRequest<ReadyGoodsRawReturnDto>;

public class ReturnReadyGoodsToRawStockCommandValidator : AbstractValidator<ReturnReadyGoodsToRawStockCommand>
{
    public ReturnReadyGoodsToRawStockCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.RawMessageId).NotEmpty()
            .WithMessage("Name the raw material lot the stock is returned to - the move always lands on a real lot.");
        RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the moved quantity in KG and/or Meter.");
        RuleFor(x => x.Reason).NotEmpty()
            .WithMessage("A reason is required to move ready goods back into raw material stock. نقل مخزون جاهز إلى الخام يتطلب سببًا.");
    }
}

public class ReturnReadyGoodsToRawStockCommandHandler
    : IRequestHandler<ReturnReadyGoodsToRawStockCommand, ReadyGoodsRawReturnDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public ReturnReadyGoodsToRawStockCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
        _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<ReadyGoodsRawReturnDto> Handle(
        ReturnReadyGoodsToRawStockCommand request, CancellationToken cancellationToken)
    {
        // The move posts stock, so it must fall outside any closed period.
        await using var periodGuard = await _periodClose.BeginPostingAsync(_clock.UtcNow, cancellationToken);

        var order = await _db.ProductionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        if (order.Status == ProductionOrderStatus.Cancelled)
            throw new DocumentLockedException("Production Order", order.OrderNumber);

        // A ready-goods return is item-specific. Never let a caller use another item's
        // balance on the same order as the source for a raw-stock receipt.
        if (order.ItemId != request.ItemId)
            throw new DomainException(
                $"Item '{request.ItemId}' does not match the item on Job Order '{order.OrderNumber}'.");

        var message = await _db.RawMessages.AsNoTracking()
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        // The destination must be a lot that actually received this item. Checking only
        // the lot's customer and warehouse would otherwise allow the ledger to credit an
        // unrelated item into a raw lot that never contained it.
        if (!message.Lines.Any(line => line.ItemId == request.ItemId))
            throw new DomainException(
                $"Lot '{message.MessageNumber}' does not contain the item being returned.");

        // Returned-to-raw stock is the customer's material again, so it must land on that customer's own
        // lot: crediting someone else's lot would move stock between accounts.
        if (message.CustomerId != order.CustomerId)
            throw new DomainException(
                $"Lot '{message.MessageNumber}' belongs to a different customer than Job Order '{order.OrderNumber}'. " +
                "Ready goods can only be returned to their own customer's raw material stock.");

        var destinationWarehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == message.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", message.WarehouseId);

        // The destination has to be a RAW MATERIAL warehouse - this movement is defined by the stock
        // changing sides, so landing anywhere else would be a different (and wrong) operation.
        if (destinationWarehouse.Kind != WarehouseKind.RawMaterial)
            throw new DomainException(
                $"Lot '{message.MessageNumber}' is not in a raw material warehouse, so it cannot receive ready goods as raw stock.");

        // A brand new IN row is posted into both warehouses, so both must still be active - history
        // already posted is never blocked or rewritten, only the new movement is refused.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, destinationWarehouse.Id, cancellationToken, "to receive ready goods back into raw material stock");

        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Kind == WarehouseKind.ReadyGoods)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, readyWarehouse.Id, cancellationToken, "to move ready goods out of it");

        // Spec sections 5-6: the moved quantity may only use the units configured on the item, and both
        // when the item requires both.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.ItemId, request.QuantityKg, request.QuantityMeter,
            cancellationToken, "on this ready-goods-to-raw movement");

        // Lock the ready lot (what leaves) AND the raw lot (what arrives), using the same key shapes the
        // delivery/transfer and the raw issue use, so a movement cannot interleave with either.
        var lockKeys = new List<StockLockKey>
        {
            StockLockKey.ReadyLot(request.ItemId, order.Id),
            StockLockKey.RawLot(message.WarehouseId, request.ItemId, message.CustomerId, message.Id)
        };

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            // Availability is checked on the exact dimension the ready balance is read from, so stock
            // that is already promised to a delivery is never double-spent.
            var availableCategories = await _db.ReadyGoodsTransfers.AsNoTracking()
                .Where(t => t.ProductionOrderId == order.Id
                         && t.ItemId == request.ItemId
                         && t.Status != ReadyGoodsTransferStatus.Cancelled)
                .Select(t => t.Category)
                .Distinct()
                .ToListAsync(cancellationToken);
            ReadyGoodsCategory? readyCategory;
            if (request.Category.HasValue)
            {
                if (!availableCategories.Contains(request.Category))
                    throw new DomainException("The selected category is not available for this ready-goods order.");
                readyCategory = request.Category;
            }
            else if (availableCategories.Count == 1)
            {
                // A single historical null category remains returnable as unclassified.
                readyCategory = availableCategories[0];
            }
            else if (availableCategories.Count == 0)
            {
                // Legacy ledger-only balances may have no transfer document at all.
                // Keep that stream returnable as unclassified; new ready-goods postings
                // always create a category-bearing transfer document.
                readyCategory = null;
            }
            else
            {
                // Null/unclassified is still a category dimension. If it coexists with
                // any named category, the caller must select explicitly rather than
                // silently consuming one stream.
                throw new DomainException("Select the ready-goods category to return; this order has multiple categories.");
            }

            var readyRows = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == readyWarehouse.Id
                         && t.RawMessageId == null
                         && t.ProductionOrderId == order.Id
                         && t.ItemId == request.ItemId
                         && t.ReadyGoodsCategory == readyCategory)
                .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                .ToListAsync(cancellationToken);

            var availableKg = readyRows.Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction);
            var availableMeter = readyRows.Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction);

            if (request.QuantityKg is > 0 && request.QuantityKg.Value > availableKg)
                throw new NegativeStockException(availableKg, request.QuantityKg.Value);
            if (request.QuantityMeter is > 0 && request.QuantityMeter.Value > availableMeter)
                throw new NegativeStockException(availableMeter, request.QuantityMeter.Value);

            var movementNumber = await _numberGenerator.NextAsync(
                DocumentType.ReadyGoodsRawReturn, cancellationToken: cancellationToken);

            var movement = new ReadyGoodsRawReturn(
                movementNumber, _clock.UtcNow, order.Id, order.CustomerId, request.ItemId,
                readyWarehouse.Id, message.Id, message.WarehouseId,
                request.QuantityKg, request.QuantityMeter, request.Reason, _currentUser.UserName, request.Notes,
                category: readyCategory);

            _db.ReadyGoodsRawReturns.Add(movement);

            // OUT of Ready Goods...
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ReadyGoodsRawReturn, movement.MovementNumber, movement.Id, _clock.UtcNow,
                readyWarehouse.Id, order.CustomerId, request.ItemId,
                rawMessageId: null, productionOrderId: order.Id,
                quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName,
                readyGoodsCategory: readyCategory));

            // ...and INTO the raw material lot, in the same transaction.
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ReadyGoodsRawReturn, movement.MovementNumber, movement.Id, _clock.UtcNow,
                message.WarehouseId, message.CustomerId, request.ItemId,
                rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
                direction: TransactionDirection.In, createdBy: _currentUser.UserName));

            _db.AuditLogEntries.Add(new AuditLogEntry(
                _clock.UtcNow, _currentUser.UserName, "ReturnReadyGoodsToRaw", "ReadyGoodsRawReturn",
                movement.Id.ToString(), beforeDataJson: null,
                afterDataJson: JsonSerializer.Serialize(new
                {
                    movementNumber = movement.MovementNumber,
                    productionOrderId = order.Id,
                    productionOrderNumber = order.OrderNumber,
                    itemId = request.ItemId,
                    sourceWarehouseId = readyWarehouse.Id,
                    destinationRawMessageId = message.Id,
                    destinationMessageNumber = message.MessageNumber,
                    quantityKg = request.QuantityKg,
                    quantityMeter = request.QuantityMeter,
                    category = readyCategory?.ToString()
                }),
                ipAddress: null, reason: request.Reason));

            await _db.SaveChangesAsync(cancellationToken);

            // A lot that had been fully consumed reads as Depleted, which would keep the material
            // unusable. Recompute the affected lot's status from the ledger so what arrived is
            // genuinely allocatable again - the same move the customer-return workflow makes.
            var trackedMessage = await _db.RawMessages
                .FirstOrDefaultAsync(m => m.Id == message.Id, cancellationToken);

            if (trackedMessage is not null)
            {
                var lotRows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == message.Id && t.WarehouseId == message.WarehouseId)
                    .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                    .ToListAsync(cancellationToken);

                var remainingKg = lotRows.Sum(t => (t.QuantityKg ?? 0) * (int)t.Direction);
                var remainingMeter = lotRows.Sum(t => (t.QuantityMeter ?? 0) * (int)t.Direction);
                var hasOut = lotRows.Any(t => t.Direction == TransactionDirection.Out);

                trackedMessage.RecalculateStatus(remainingKg > 0 || remainingMeter > 0, hasOut);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new ReadyGoodsRawReturnDto
            {
                Id = movement.Id,
                MovementNumber = movement.MovementNumber,
                MovementDate = movement.MovementDate,
                ProductionOrderId = order.Id,
                ProductionOrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                ItemId = request.ItemId,
                SourceWarehouseId = readyWarehouse.Id,
                SourceWarehouseName = readyWarehouse.Name,
                DestinationRawMessageId = message.Id,
                DestinationMessageNumber = message.MessageNumber,
                DestinationWarehouseId = message.WarehouseId,
                DestinationWarehouseName = destinationWarehouse.Name,
                QuantityKg = movement.QuantityKg,
                QuantityMeter = movement.QuantityMeter,
                Category = movement.Category?.ToString(),
                Reason = movement.Reason,
                Notes = movement.Notes,
                CreatedBy = movement.CreatedBy,
                CreatedAtUtc = movement.CreatedAtUtc
            };
        }
    }
}
