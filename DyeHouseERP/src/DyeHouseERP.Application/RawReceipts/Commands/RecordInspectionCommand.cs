using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawReceipts.Commands;

/// <summary>
/// Records receiving inspection information (spec section 9). This is NOT an
/// approval gate - the receipt already posted and is already allocatable. It only
/// records the result, the inspector, notes, and any rejected quantity per line.
/// A rejected quantity is corrected on the ledger with an equal-and-opposite OUT
/// row (never a balance edit), so rejected material stays traceable to this
/// customer's receipt while no longer being available for allocation.
/// </summary>
public record RecordInspectionCommand(
    Guid RawMessageId,
    InspectionStatus Result,
    string? Notes,
    List<RawMessageLineRejectionInput>? Rejections = null) : IRequest<Unit>;

public class RecordInspectionCommandValidator : AbstractValidator<RecordInspectionCommand>
{
    public RecordInspectionCommandValidator()
    {
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.Result).IsInEnum().NotEqual(InspectionStatus.PendingInspection);
        RuleFor(x => x.Notes).MaximumLength(1000);

        // A pure "Accepted" result cannot carry rejected quantities - recording a
        // rejection means the outcome is at best a partial acceptance.
        RuleFor(x => x.Result)
            .Must(result => result != InspectionStatus.Accepted)
            .When(x => x.Rejections is { Count: > 0 })
            .WithMessage("Rejected quantities require a Rejected or Accepted With Notes inspection result.");

        RuleForEach(x => x.Rejections).ChildRules(rejection =>
        {
            rejection.RuleFor(r => r.LineId).NotEmpty();
            rejection.RuleFor(r => r)
                .Must(r => r.RejectedQuantityKg is > 0 || r.RejectedQuantityMeter is > 0)
                .WithMessage("A rejected quantity must be greater than zero in KG and/or Meter.");
            rejection.RuleFor(r => r.RejectedQuantityKg).GreaterThanOrEqualTo(0).When(r => r.RejectedQuantityKg.HasValue);
            rejection.RuleFor(r => r.RejectedQuantityMeter).GreaterThanOrEqualTo(0).When(r => r.RejectedQuantityMeter.HasValue);
        });
    }
}

public class RecordInspectionCommandHandler : IRequestHandler<RecordInspectionCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public RecordInspectionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<Unit> Handle(RecordInspectionCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: rejecting part of a receipt posts a correcting OUT row,
        // so the inspection date must fall outside any closed period.
        await using var periodGuard = await _periodClose.BeginPostingAsync(_clock.UtcNow, cancellationToken);

        // No correcting ledger row is written when nothing is rejected, so a plain
        // inspection result needs neither a lot lock nor a warehouse movement check.
        if (request.Rejections is not { Count: > 0 })
        {
            var inspected = await _db.RawMessages
                .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
                ?? throw new NotFoundException("RawMessage", request.RawMessageId);

            inspected.RecordInspection(request.Result, _currentUser.UserName, request.Notes, _clock.UtcNow);

            await _db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }

        var rejections = request.Rejections!;

        // The message graph is read TWICE. First as a read-only snapshot, only to resolve
        // the lot dimensions that must be locked. Then - inside the lock - as the tracked
        // aggregate that actually records the rejection.
        //
        // H7 (concurrency): this is what closes the check-then-act window. A rejection
        // ACCUMULATES on the line and is capped by the received quantity, so the OUT row a
        // request posts is only correct against the COMMITTED line state. Recording the
        // rejection from an aggregate loaded before the lock let two racing inspections see
        // the same starting point and each post its own correcting OUT row: the customer's
        // raw stock was deducted twice for one rejection and the recorded total could pass
        // the received quantity. The lock alone does not help - it is the RE-READ under the
        // lock (the same pattern the allocation path uses) that makes the second request
        // compute its delta against what the first one actually committed.
        var snapshot = await _db.RawMessages.AsNoTracking()
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        // A retired warehouse never receives a new stock row: a rejection posts a
        // correcting OUT row against the message's warehouse, so that warehouse must
        // still be active. Reading the message and its inspection history stays possible.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, snapshot.WarehouseId, cancellationToken, "to record a rejected quantity against it");

        // Spec sections 5-6: a rejected quantity is a quantity of the line's item, so
        // it may only use that item's configured units.
        var rejectionLineItems = snapshot.Lines.ToDictionary(l => l.Id, l => l.ItemId);
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db,
            rejections.Where(r => rejectionLineItems.ContainsKey(r.LineId))
                .Select(r => (rejectionLineItems[r.LineId], r.RejectedQuantityKg, r.RejectedQuantityMeter)),
            cancellationToken, "as a rejected quantity");

        // H6: a rejected quantity posts a correcting OUT row on the same raw
        // lot, so it takes the same lot locks the allocation/adjustment paths
        // use - a rejection and a concurrent allocation can no longer both
        // act on a stale view of the same balance.
        var rejectedLineIds = rejections.Select(r => r.LineId).ToHashSet();
        var lockKeys = snapshot.Lines
            .Where(l => rejectedLineIds.Contains(l.Id))
            .Select(l => StockLockKey.RawLot(snapshot.WarehouseId, l.ItemId, snapshot.CustomerId, snapshot.Id))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            var message = await _db.RawMessages
                .Include(m => m.Lines)
                .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
                ?? throw new NotFoundException("RawMessage", request.RawMessageId);

            message.RecordInspection(request.Result, _currentUser.UserName, request.Notes, _clock.UtcNow);

            foreach (var rejection in rejections)
            {
                var (deltaKg, deltaMeter) = message.RecordLineRejection(
                    rejection.LineId, rejection.RejectedQuantityKg, rejection.RejectedQuantityMeter);

                if (deltaKg is null && deltaMeter is null)
                    continue;

                var line = message.Lines.First(l => l.Id == rejection.LineId);

                // Rejection and allocation share this lock. Check the live remaining
                // balance, not only the original received quantity: already consumed raw
                // must not be deducted again by a later inspection.
                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == message.Id && t.ItemId == line.ItemId &&
                        t.CustomerId == message.CustomerId && t.WarehouseId == message.WarehouseId)
                    .Select(t => new { t.Id, t.QuantityKg, t.QuantityMeter, t.Direction }).ToListAsync(cancellationToken);
                var persistedIds = rows.Select(t => t.Id).ToHashSet();
                var pending = _db.InventoryTransactions.Local.Where(t => t.RawMessageId == message.Id &&
                    t.ItemId == line.ItemId && t.CustomerId == message.CustomerId && t.WarehouseId == message.WarehouseId && !persistedIds.Contains(t.Id));
                var kg = rows.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction) + pending.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);
                var meter = rows.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction) + pending.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction);
                if (deltaKg > kg) throw new Domain.Exceptions.NegativeStockException(kg, deltaKg.Value);
                if (deltaMeter > meter) throw new Domain.Exceptions.NegativeStockException(meter, deltaMeter.Value);

                // The receipt posted the full received quantity as an IN row. The
                // rejected part is corrected with one OUT row referencing the same
                // message, so the message/customer balance (a live signed sum) drops
                // by exactly the rejected amount and the rejection is auditable.
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.RawReceiptMessage, message.MessageNumber, message.Id,
                    _clock.UtcNow, message.WarehouseId, message.CustomerId, line.ItemId,
                    rawMessageId: message.Id, productionOrderId: null,
                    quantityKg: deltaKg, quantityMeter: deltaMeter,
                    direction: TransactionDirection.Out,
                    createdBy: _currentUser.UserName));
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
