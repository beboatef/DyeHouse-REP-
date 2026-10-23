using System.Text.Json;
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

namespace DyeHouseERP.Application.ProductionOrders.Commands;

// =====================================================================
// Cancellation (S24)
// =====================================================================

/// <summary>
/// Cancels a Job Order (أمر تشغيل) and reverses the stock the order itself
/// created, using the same append-only ledger machinery every other reversal in
/// the system uses (spec section 18):
///
///   * issuing raw material to a Job Order posts an OUT row
///     (DocumentType.ProductionOrder);
///   * a cancellation posts the equal-and-opposite IN row for whatever the
///     order still holds - its OUTSTANDING issue, derived by
///     <see cref="ProductionOrderIssueLedger"/> exactly as a pause release is.
///
/// Nothing is ever edited or deleted: the original OUT rows stay exactly as
/// posted, and the reversal is a NEW row, so the ledger keeps a complete record
/// of every cycle. Deriving the return from the ledger (rather than reversing
/// each OUT row 1:1) is what makes cancellation after a partial pause correct -
/// material a pause already gave back is never credited a second time.
///
/// What is deliberately NOT reversed: ready-goods rows belong to
/// ReadyGoodsTransfer documents, which post their own stock and have their own
/// cancellation with its own reversal rows; external-release rows are WIP
/// consumption by an explicit business rule. Reversing those here would
/// double-credit the same lot against their own cancellations.
///
/// Idempotency: the entity refuses a second Cancel, the live status is re-read
/// INSIDE the stock locks (R3), and the reversal amounts are re-derived INSIDE
/// those locks - so a repeated or concurrent cancellation can never post a
/// second reversal set. A completed order cannot be cancelled (its output has
/// left the raw dimension entirely).
/// </summary>
public record CancelProductionOrderCommand(Guid ProductionOrderId, string Reason) : IRequest<ProductionOrderDto>;

public class CancelProductionOrderCommandValidator : AbstractValidator<CancelProductionOrderCommand>
{
    public CancelProductionOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty()
            .WithMessage("Cancelling a production order requires a reason.");
    }
}

public class CancelProductionOrderCommandHandler : IRequestHandler<CancelProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CancelProductionOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<ProductionOrderDto> Handle(CancelProductionOrderCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: the reversal rows are dated today, so a closed period
        // only blocks the cancellation if TODAY itself is closed - historical
        // corrections stay possible (same convention as every other reversal).
        // One clock read: guard, the compensating IN rows and the audit note all
        // carry the identical posting date.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        // Fast refusals before any lock is taken. The domain re-checks both
        // inside the lock - these only avoid queueing for an impossible cancel.
        if (order.Status == ProductionOrderStatus.Cancelled)
            throw new DomainException($"Production order {order.OrderNumber} is already cancelled.");
        if (order.Status == ProductionOrderStatus.Completed)
            throw new DocumentLockedException("Production Order", order.OrderNumber);

        // Spec section 9, case C: production has already consumed some of this material, so
        // the ordinary UNUSED-RAW cancellation is refused outright - direct API calls included.
        // Processed material is not unused raw material: unwinding it here would return WIP to
        // the raw warehouse as if it had never been worked and would double-count the stock.
        await EnsureProductionNotStartedAsync(_db, order.Id, order.Status, order.OrderNumber, cancellationToken);

        // ---- Lock EVERY lot this order could still hold a claim on ----
        // The allocation rows are the authoritative list of lots the order has
        // ever drawn from; the ledger's own outstanding lots are added as a
        // superset. Each key has the exact shape every other raw-dimension
        // poster uses (R1), so a concurrent pause release, resume issue,
        // allocation or stock adjustment on any of these lots queues behind us.
        var allocations = (await _db.RawAllocations.AsNoTracking()
            .Where(a => a.ProductionOrderId == order.Id)
            .Select(a => new { a.RawMessageId, a.ItemId })
            .ToListAsync(cancellationToken))
            .Select(a => (MessageId: a.RawMessageId, ItemId: a.ItemId))
            .ToList();

        var preOutstanding = await ProductionOrderIssueLedger.GetOutstandingIssuePerLotAsync(_db, order.Id, cancellationToken);

        var lotKeys = allocations
            .Concat(preOutstanding.Keys)
            .Distinct()
            .ToList();

        var messageIds = lotKeys.Select(l => l.MessageId).Distinct().ToList();
        var messages = await _db.RawMessages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var lockKeys = lotKeys
            .Where(l => messages.ContainsKey(l.MessageId))
            .Select(l => StockLockKey.RawLot(messages[l.MessageId].WarehouseId, l.ItemId, order.CustomerId, l.MessageId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            // R3: the live status is re-read INSIDE the lock, so a second
            // cancellation that was queueing behind this one cannot run twice -
            // it sees Cancelled here and is refused before posting anything.
            var liveStatus = await _db.ProductionOrders.AsNoTracking()
                .Where(o => o.Id == order.Id)
                .Select(o => (ProductionOrderStatus?)o.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (liveStatus == ProductionOrderStatus.Cancelled)
                throw new DomainException($"Production order {order.OrderNumber} is already cancelled.");
            if (liveStatus is null)
                throw new NotFoundException("ProductionOrder", order.Id);
            if (liveStatus == ProductionOrderStatus.Completed)
                throw new DocumentLockedException("Production Order", order.OrderNumber);

            // Case C is re-checked INSIDE the lock too: a stage movement that committed while
            // this request was queueing must still block the unused-raw cancellation.
            await EnsureProductionNotStartedAsync(_db, order.Id, liveStatus.Value, order.OrderNumber, cancellationToken);

            // The outstanding issue is re-derived INSIDE the lock: a release or
            // re-issue that committed while we were queueing changes what is
            // owed, and returning the stale figure would double-credit the lot.
            var outstanding = await ProductionOrderIssueLedger.GetOutstandingIssuePerLotAsync(_db, order.Id, cancellationToken);

            // A retired warehouse is never a way to reverse raw material: cancellation posts
            // one compensating IN row per lot that is still owed, so every warehouse that will
            // actually receive a row must still be active. Checked HERE, against the lots that
            // are genuinely outstanding and before any row is written, for two reasons: the
            // refusal then has no side effect at all, and an order that owes nothing still
            // cancels normally - deactivating a warehouse must not block closing an order that
            // would return no material.
            foreach (var warehouseId in outstanding
                         .Where(l => l.Value.Kg > 0 || l.Value.Meter > 0)
                         .Select(l => messages[l.Key.MessageId].WarehouseId)
                         .Distinct())
                await WarehouseGuard.EnsureUsableForNewMovementAsync(
                    _db, warehouseId, cancellationToken, "to reverse outstanding raw material back into it");

            var returned = new List<object>();
            foreach (var lot in outstanding.Where(l => l.Value.Kg > 0 || l.Value.Meter > 0))
            {
                var message = messages[lot.Key.MessageId];
                var (kg, meter) = lot.Value;

                // One compensating IN row per source lot. KG and Meter stay in
                // their own columns - never converted, never summed together.
                // ReversesTransactionId is deliberately null: this row is the NET
                // of several OUT rows (and of any earlier pause release), so
                // linking it to one of them would misstate which row it nullifies.
                // It remains fully traceable through SourceDocumentId/Number
                // (the order) plus the audit entry below.
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.ProductionOrder, order.OrderNumber, order.Id,
                    postingDate, message.WarehouseId, order.CustomerId, lot.Key.ItemId,
                    rawMessageId: lot.Key.MessageId, productionOrderId: order.Id,
                    quantityKg: kg > 0 ? kg : null,
                    quantityMeter: meter > 0 ? meter : null,
                    direction: TransactionDirection.In, createdBy: _currentUser.UserName));

                returned.Add(new
                {
                    rawMessageId = lot.Key.MessageId,
                    itemId = lot.Key.ItemId,
                    quantityKg = kg,
                    quantityMeter = meter
                });
            }

            var statusBefore = order.Status;

            // Final authority: the entity refuses a completed or already
            // cancelled order. Nothing has been saved yet, so a refusal here
            // leaves the ledger untouched.
            order.Cancel(request.Reason, _currentUser.UserName);

            // If this is a reprocessing order that has not entered production,
            // release its linked separate back to the pending register in the
            // same unit of work as the cancellation and raw-ledger reversal.
            if (order.ReprocessingOfProductionOrderId.HasValue)
            {
                var linkedSeparate = await _db.Separates.FirstOrDefaultAsync(
                    s => s.ReprocessingProductionOrderId == order.Id,
                    cancellationToken);
                linkedSeparate?.CancelReprocessing(order.Id, _currentUser.UserName);
            }

            // The state change and every returned lot are audited with who/when/
            // why and the exact quantities - a cancellation is never a silent
            // stock mutation (same convention as Pause).
            _db.AuditLogEntries.Add(new AuditLogEntry(
                postingDate, _currentUser.UserName, "Cancel", "ProductionOrder", order.Id.ToString(),
                JsonSerializer.Serialize(new { status = statusBefore.ToString() }),
                JsonSerializer.Serialize(new
                {
                    status = ProductionOrderStatus.Cancelled.ToString(),
                    returned
                }),
                ipAddress: null,
                reason: request.Reason));

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }

    /// <summary>
    /// The Case C guard (spec section 9): the ordinary unused-raw cancellation may only run
    /// while production has NOT started. "Started" means the InProduction state, or any stage
    /// execution that actually recorded something - an output, separates or loss, a closed or
    /// skipped stage, or a completion timestamp. The formation stage merely existing (and
    /// sitting InProgress) is the freshly issued state and is NOT production.
    /// </summary>
    private static async Task EnsureProductionNotStartedAsync(
        IApplicationDbContext db, Guid orderId, ProductionOrderStatus status, string orderNumber,
        CancellationToken cancellationToken)
    {
        var hasRecordedProduction = await db.ProductionOrderStageExecutions.AsNoTracking()
            .AnyAsync(s => s.ProductionOrderId == orderId &&
                (s.OutputKg != null || s.OutputMeter != null
                 || s.SeparatesKg != null || s.SeparatesMeter != null
                 || s.LossKg != null || s.LossMeter != null
                 || s.Status == StageExecutionStatus.Completed
                 || s.Status == StageExecutionStatus.Skipped
                 || s.CompletedAtUtc != null), cancellationToken);

        if (status == ProductionOrderStatus.InProduction || hasRecordedProduction)
            throw new DomainException(
                $"Production order {orderNumber} has already started production, so the unused raw-material cancellation " +
                "cannot be used. Processed material is not unused raw material and must never be returned to the raw " +
                "warehouse as if it were; use the explicit rework/correction workflow instead.");
    }
}
