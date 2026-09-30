using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Deliveries.Commands;

// -------------------- Prepare --------------------
public record MarkDeliveryPreparedCommand(Guid DeliveryId) : IRequest<DeliveryDto>;

public class MarkDeliveryPreparedCommandHandler : IRequestHandler<MarkDeliveryPreparedCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    public MarkDeliveryPreparedCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<DeliveryDto> Handle(MarkDeliveryPreparedCommand request, CancellationToken cancellationToken)
    {
        var delivery = await _db.Deliveries.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);
        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0) throw new DomainException("Cannot prepare a delivery with no lines.");

        delivery.MarkPrepared();
        await _db.SaveChangesAsync(cancellationToken);
        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}

// -------------------- Deliver --------------------
public record MarkDeliveryDeliveredCommand(Guid DeliveryId) : IRequest<DeliveryDto>;

/// <summary>
/// Deducts ready stock (spec section 31: "validate ready balance, prevent
/// over-delivery... deduct from ready stock, lock the document"). Each line
/// posts an OUT InventoryTransaction against the ready warehouse dimension
/// (RawMessageId = null, ProductionOrderId set) - the same ledger dimension
/// ReadyGoodsTransfer posts INs into.
///
/// H2/H3 concurrency hardening:
/// - The whole validate-then-post window runs under the global stock lock
///   (H6) for every (order, item) the delivery touches, so two deliveries
///   racing on the same ready lot can no longer both pass the balance check
///   and jointly push the lot negative.
/// - The balance check aggregates the quantities of DUPLICATE lines for the
///   same (ProductionOrder, Item) before comparing. Previously each duplicate
///   line was checked separately against the same available quantity, so a
///   delivery containing the same item twice could pass the check twice over
///   and still be double the ready balance.
/// </summary>
public class MarkDeliveryDeliveredCommandHandler : IRequestHandler<MarkDeliveryDeliveredCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public MarkDeliveryDeliveredCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<DeliveryDto> Handle(MarkDeliveryDeliveredCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: delivering ready goods is the stock posting that empties the lot.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);
        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);

        // Validate ready balance per line before posting anything. The lookup is
        // deterministic (OrderBy Id) so two requests resolving "the" ready
        // warehouse always pick the same row and post against the same dimension.
        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Kind == Domain.Entities.WarehouseKind.ReadyGoods)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        // R2: the key mirrors the balance query's own filter - (item, production
        // order), with NO warehouse and NO customer term, because the query below
        // does not filter on either. Locking on a warehouse would let a delivery
        // and a ready-goods transfer that resolved different warehouses read the
        // same balance concurrently and jointly overdraw it.
        var lockKeys = lines
            .Select(l => StockLockKey.ReadyLot(l.ItemId, l.ProductionOrderId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            // H3: aggregate duplicate lines per (ProductionOrder, Item) BEFORE
            // comparing against the available balance. Two lines for the same
            // lot must be validated as their combined quantity, otherwise each
            // one is checked against the full balance and together they can
            // consume more than exists.
            var requestedByLot = lines
                .GroupBy(l => new { l.ProductionOrderId, l.ItemId })
                .Select(g => new
                {
                    g.Key.ProductionOrderId,
                    g.Key.ItemId,
                    RequestedKg = g.Sum(l => l.QuantityKg ?? 0),
                    RequestedMeter = g.Sum(l => l.QuantityMeter ?? 0)
                })
                .ToList();

            foreach (var requested in requestedByLot)
            {
                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == null
                             && t.ProductionOrderId == requested.ProductionOrderId
                             && t.ItemId == requested.ItemId)
                    .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                    .ToListAsync(cancellationToken);

                var availableKg = rows.Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction);
                var availableMeter = rows.Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction);

                if (requested.RequestedKg > 0 && requested.RequestedKg > availableKg)
                    throw new NegativeStockException(availableKg, requested.RequestedKg);
                if (requested.RequestedMeter > 0 && requested.RequestedMeter > availableMeter)
                    throw new NegativeStockException(availableMeter, requested.RequestedMeter);
            }

            delivery.MarkDelivered();

            foreach (var line in lines)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, _clock.UtcNow,
                    readyWarehouse.Id, delivery.CustomerId, line.ItemId,
                    rawMessageId: null, productionOrderId: line.ProductionOrderId,
                    quantityKg: line.QuantityKg, quantityMeter: line.QuantityMeter,
                    direction: TransactionDirection.Out, createdBy: _currentUser.UserName));
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}

// -------------------- Cancel --------------------
public record CancelDeliveryCommand(Guid DeliveryId, string Reason) : IRequest<DeliveryDto>;

public class CancelDeliveryCommandValidator : AbstractValidator<CancelDeliveryCommand>
{
    public CancelDeliveryCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

/// <summary>Cancelling a Delivered delivery posts a full reversal (IN) of every OUT row it created - never edits or deletes the original rows (spec section 31 + 18). The reversal runs under the same stock lock as the delivery itself, so a cancellation and a (second) delivery of the same lot cannot interleave.</summary>
public class CancelDeliveryCommandHandler : IRequestHandler<CancelDeliveryCommand, DeliveryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CancelDeliveryCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<DeliveryDto> Handle(CancelDeliveryCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal row is dated today, so a closed period only
        // blocks it if today itself is closed - historical corrections stay possible.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        var wasDelivered = delivery.Status == DeliveryStatus.Delivered;

        if (wasDelivered)
        {
            var originalRows = await _db.InventoryTransactions
                .Where(t => t.SourceDocumentId == delivery.Id && t.SourceDocumentType == DocumentType.Delivery)
                .ToListAsync(cancellationToken);

            // Lock exactly the lots the reversal will credit back, using the
            // same key shape the delivery itself locks (R2) so a cancel and a
            // delivery of the same order are mutually exclusive.
            var lockKeys = originalRows
                .Select(r => StockLockKey.ReadyLot(r.ItemId, r.ProductionOrderId ?? Guid.Empty))
                .Distinct()
                .ToList();

            await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
            {
                foreach (var row in originalRows)
                {
                    _db.InventoryTransactions.Add(new InventoryTransaction(
                        DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, _clock.UtcNow,
                        row.WarehouseId, row.CustomerId, row.ItemId, row.RawMessageId, row.ProductionOrderId,
                        row.QuantityKg, row.QuantityMeter,
                        direction: row.Direction == TransactionDirection.Out ? TransactionDirection.In : TransactionDirection.Out,
                        createdBy: _currentUser.UserName, reversesTransactionId: row.Id));
                }

                delivery.Cancel(request.Reason, _currentUser.UserName);

                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            delivery.Cancel(request.Reason, _currentUser.UserName);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
    }
}
