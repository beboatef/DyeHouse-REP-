using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
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
/// <summary>
/// Posts the physical delivery (the OUT rows that empty the ready lot). <paramref name="RequestId"/> is
/// the optional client idempotency token: a double-submitted or retried confirmation carrying the SAME
/// token returns the delivery that already posted instead of erroring out - and never posts a second
/// deduction. The unique filtered index on the token is the database backstop for a true race.
/// </summary>
public record MarkDeliveryDeliveredCommand(Guid DeliveryId, Guid? RequestId = null) : IRequest<DeliveryDto>;

/// <summary>
/// Deducts ready stock (spec section 31: "validate ready balance, prevent
/// over-delivery... deduct from ready stock, lock the document"). Each line
/// posts an OUT InventoryTransaction against the ready warehouse dimension
/// (RawMessageId = null, ProductionOrderId set) - the same ledger dimension
/// ReadyGoodsTransfer posts INs into.
///
/// A SUB-customer permit is the one exception: it owns no movement (the primary
/// permit it is issued against already posted it), so confirming one only records
/// the document's state.
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
        // ---- Idempotency (spec section 31) -----------------------------------
        // A retry that carries the token of a confirmation which already posted answers with THAT
        // document and writes nothing: the stock is deducted exactly once, however many times the
        // confirmation is sent. Checked before the period guard so a retry still succeeds after the
        // period closes, and before the state guard so a retry is not mistaken for a duplicate post.
        if (request.RequestId.HasValue)
        {
            var alreadyPostedId = await _db.Deliveries.AsNoTracking()
                .Where(d => d.DeliverRequestId == request.RequestId.Value)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (alreadyPostedId.HasValue)
                return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, alreadyPostedId.Value, cancellationToken);
        }

        // Spec section 43: delivering ready goods is the stock posting that empties the lot.
        // The clock is read ONCE so the guard and every ledger row below carry the same date.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        // RC#3: reject on document state BEFORE the ready-balance check. After
        // the first successful delivery the lot balance is zero, so a duplicate
        // request used to surface as NegativeStockException (409) instead of the
        // state machine's DomainException (422). This is only a check reordering:
        // locking, balance validation and the stock deduction below are all
        // unchanged, and MarkDelivered's own state guard remains as a backstop.
        if (delivery.Status != DeliveryStatus.Prepared)
            throw new DomainException($"Delivery is {delivery.Status}, expected Prepared.");

        // A draft created before the separates rule changed, or a manually submitted document,
        // must not bypass the rule at posting time. Separates are reprocessed, never delivered.
        var containsSeparates = await _db.DeliveryLines.AsNoTracking()
            .AnyAsync(line => line.DeliveryId == delivery.Id
                           && line.Category == ReadyGoodsCategory.Separates,
                cancellationToken);
        if (containsSeparates)
            throw new DomainException(
                "This delivery contains separates, which cannot be delivered to the customer. Create a linked reprocessing production order instead. المنفصلات مخصصة لإعادة التشغيل ولا تُسلَّم للعميل.");

        // ---- Sub-customer permit (spec section 31) -----------------------------------
        // The PRIMARY permit is the sole owner of the physical inventory movement. A sub-customer
        // permit is a LINKED document: confirming it records the document's own state and posts
        // nothing at all. Without this, confirming it would deduct the very same ready lot a second
        // time - the primary permit's OUT rows are the only ones that may exist for that delivery.
        //
        // It is still validated exactly like any other permit at the moment it takes effect - the same
        // discipline the primary permit follows when its ready balance is checked here and only here:
        // the permit it slices must itself have been DELIVERED, and the quantity it hands over must still
        // fit inside what that permit delivered (a sibling may have claimed the rest, or the primary may
        // have been corrected downward since this one was drafted).
        if (delivery.IsSubCustomerPermit)
        {
            var primary = (await SubCustomerPermitGuard.ResolvePrimaryAsync(
                _db, delivery.PrimaryDeliveryId, cancellationToken))!;
            SubCustomerPermitGuard.EnsurePrimaryIsDelivered(primary);

            var ownLines = await _db.DeliveryLines.AsNoTracking()
                .Where(l => l.DeliveryId == delivery.Id)
                .Select(l => new { l.ProductionOrderId, l.ItemId, l.QuantityKg, l.QuantityMeter })
                .ToListAsync(cancellationToken);

            await SubCustomerPermitGuard.EnsureWithinPrimaryAsync(
                _db, primary,
                ownLines.Select(l => (l.ProductionOrderId, l.ItemId, l.QuantityKg ?? 0m, l.QuantityMeter ?? 0m)).ToList(),
                cancellationToken,
                // This permit's own stored lines are not a "sibling" claim against itself.
                excludeSubPermitId: delivery.Id);

            delivery.MarkDelivered(request.RequestId);
            await _db.SaveChangesAsync(cancellationToken);
            return await GetDeliveriesQueryHandler.LoadDtoAsync(_db, delivery.Id, cancellationToken);
        }

        var lines = await _db.DeliveryLines.Where(l => l.DeliveryId == delivery.Id).ToListAsync(cancellationToken);

        // Validate ready balance per line before posting anything. The lookup is
        // deterministic (OrderBy Id) so two requests resolving "the" ready
        // warehouse always pick the same row and post against the same dimension.
        var readyWarehouse = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Kind == Domain.Entities.WarehouseKind.ReadyGoods)
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("No Ready Goods warehouse is configured.");

        // A deactivated warehouse keeps its history but never receives a fresh stock row,
        // and delivering posts one OUT row per line against this warehouse - so the state
        // is checked here, before the stock lock and before anything is written.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, readyWarehouse.Id, cancellationToken, "to deliver ready goods out of it");

        // R2: serialize movements for the same order/item lot. The balance check below
        // is scoped to the selected Ready Goods warehouse and category; using the
        // broader lot lock also prevents concurrent category transfers for one order
        // from racing against delivery or return movements.
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
            var transferCategoriesByOrder = await _db.ReadyGoodsTransfers.AsNoTracking()
                .Where(t => lines.Select(l => l.ProductionOrderId).Contains(t.ProductionOrderId)
                         && t.Status != ReadyGoodsTransferStatus.Cancelled)
                .Select(t => new { t.ProductionOrderId, t.Category })
                .ToListAsync(cancellationToken);
            var categoriesByOrder = transferCategoriesByOrder
                .GroupBy(t => t.ProductionOrderId)
                .ToDictionary(g => g.Key, g => g.Select(t => t.Category).Distinct().ToList());

            // Keep historical, unclassified ledger stock deliverable even when the same order
            // also has newly classified transfers. The delivery UI represents that legacy lot
            // with an empty category, so resolve it against the actual null-category balance
            // before applying the single-category compatibility fallback.
            var orderIdsForLines = lines.Select(l => l.ProductionOrderId).Distinct().ToList();
            var itemIdsForLines = lines.Select(l => l.ItemId).Distinct().ToList();
            var unclassifiedRows = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == readyWarehouse.Id
                         && t.RawMessageId == null
                         && t.ProductionOrderId != null
                         && orderIdsForLines.Contains(t.ProductionOrderId.Value)
                         && itemIdsForLines.Contains(t.ItemId)
                         && t.ReadyGoodsCategory == null)
                .Select(t => new { OrderId = t.ProductionOrderId!.Value, t.ItemId, t.QuantityKg, t.QuantityMeter, t.Direction })
                .ToListAsync(cancellationToken);
            var unclassifiedBalances = unclassifiedRows
                .GroupBy(t => (t.OrderId, t.ItemId))
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Kg: g.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction),
                        Meter: g.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction)));

            var categoryByLine = new Dictionary<Guid, ReadyGoodsCategory?>();

            foreach (var line in lines)
            {
                var availableCategories = categoriesByOrder.GetValueOrDefault(line.ProductionOrderId) ?? new List<ReadyGoodsCategory?>();
                ReadyGoodsCategory? selectedCategory = line.Category;
                if (selectedCategory.HasValue)
                {
                    if (!availableCategories.Contains(selectedCategory))
                        throw new DomainException($"Production order has no ready-goods lot classified as {selectedCategory}.");
                }
                else
                {
                    var hasUnclassifiedBalance = unclassifiedBalances.TryGetValue((line.ProductionOrderId, line.ItemId), out var unclassified);
                    var hasUnclassifiedStock = hasUnclassifiedBalance
                        && (unclassified.Kg > 0 || unclassified.Meter > 0);

                    if (hasUnclassifiedStock || availableCategories.Count == 0)
                    {
                        // The UI's unclassified option is represented by null. A null-category
                        // ledger lot remains distinct from FirstGrade/SecondGrade/Separates.
                        selectedCategory = null;
                    }
                    else if (availableCategories.Count == 1)
                    {
                        // Backward compatibility for older API clients omitting category when
                        // exactly one classified category exists and no matching legacy lot fits.
                        selectedCategory = availableCategories[0];
                    }
                    else
                    {
                        throw new DomainException("Select the ready-goods category for each delivery line; this production order has multiple categories.");
                    }
                }
                categoryByLine[line.Id] = selectedCategory;
            }

            var requestedByLot = lines
                .GroupBy(l => new { l.ProductionOrderId, l.ItemId, Category = categoryByLine[l.Id] })
                .Select(g => new
                {
                    g.Key.ProductionOrderId,
                    g.Key.ItemId,
                    g.Key.Category,
                    RequestedKg = g.Sum(l => l.QuantityKg ?? 0),
                    RequestedMeter = g.Sum(l => l.QuantityMeter ?? 0)
                })
                .ToList();

            foreach (var requested in requestedByLot)
            {
                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.WarehouseId == readyWarehouse.Id
                             && t.RawMessageId == null
                             && t.ProductionOrderId == requested.ProductionOrderId
                             && t.ItemId == requested.ItemId
                             && t.ReadyGoodsCategory == requested.Category)
                    .Select(t => new { t.QuantityKg, t.QuantityMeter, t.Direction })
                    .ToListAsync(cancellationToken);

                var availableKg = rows.Sum(r => (r.QuantityKg ?? 0) * (int)r.Direction);
                var availableMeter = rows.Sum(r => (r.QuantityMeter ?? 0) * (int)r.Direction);

                if (requested.RequestedKg > 0 && requested.RequestedKg > availableKg)
                    throw new NegativeStockException(availableKg, requested.RequestedKg);
                if (requested.RequestedMeter > 0 && requested.RequestedMeter > availableMeter)
                    throw new NegativeStockException(availableMeter, requested.RequestedMeter);
            }

            delivery.MarkDelivered(request.RequestId);

            foreach (var line in lines)
            {
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, postingDate,
                    readyWarehouse.Id, delivery.CustomerId, line.ItemId,
                    rawMessageId: null, productionOrderId: line.ProductionOrderId,
                    quantityKg: line.QuantityKg, quantityMeter: line.QuantityMeter,
                    direction: TransactionDirection.Out, createdBy: _currentUser.UserName,
                    readyGoodsCategory: categoryByLine[line.Id]));
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
        // One clock read for guard and reversal rows alike.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var delivery = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", request.DeliveryId);

        // The chain stays consistent in one direction: a PRIMARY permit with live sub-customer permits may
        // not be cancelled, because cancelling it reverses the one physical movement those sub-permits are a
        // slice of - they would then describe goods that were never handed over. The refusal names them, and
        // the user cancels them first (each of those is a document action with its own audit trail, so no
        // cascade silently rewrites other people's documents).
        if (!delivery.IsSubCustomerPermit)
        {
            var liveSubPermits = await _db.Deliveries.AsNoTracking()
                .Where(d => d.PrimaryDeliveryId == delivery.Id && d.Status != DeliveryStatus.Cancelled)
                .Select(d => d.DeliveryNumber)
                .ToListAsync(cancellationToken);

            if (liveSubPermits.Count > 0)
                throw new DomainException(
                    $"Permit {delivery.DeliveryNumber} has sub-customer permits issued against it " +
                    $"({string.Join(", ", liveSubPermits)}), so it cannot be cancelled: cancelling it would reverse the " +
                    "stock movement those permits are a slice of. Cancel the sub-customer permits first. " +
                    "لا يمكن إلغاء الإذن الأساسي قبل إلغاء الأذون الفرعية الصادرة عنه.");
        }

        var wasDelivered = delivery.Status == DeliveryStatus.Delivered;

        if (wasDelivered)
        {
            var originalRows = await _db.InventoryTransactions
                .Where(t => t.SourceDocumentId == delivery.Id && t.SourceDocumentType == DocumentType.Delivery)
                .ToListAsync(cancellationToken);

            // The reversal is itself a new posting: it credits stock back into the warehouses
            // the original OUT rows left from, so each of them must still be active. History is
            // never blocked or rewritten - only the new row is refused.
            foreach (var reversalWarehouseId in originalRows.Select(r => r.WarehouseId).Distinct())
                await WarehouseGuard.EnsureUsableForNewMovementAsync(
                    _db, reversalWarehouseId, cancellationToken, "to reverse a delivery into it");

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
                        DocumentType.Delivery, delivery.DeliveryNumber, delivery.Id, postingDate,
                        row.WarehouseId, row.CustomerId, row.ItemId, row.RawMessageId, row.ProductionOrderId,
                        row.QuantityKg, row.QuantityMeter,
                        direction: row.Direction == TransactionDirection.Out ? TransactionDirection.In : TransactionDirection.Out,
                        createdBy: _currentUser.UserName, reversesTransactionId: row.Id,
                        readyGoodsCategory: row.ReadyGoodsCategory));
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
