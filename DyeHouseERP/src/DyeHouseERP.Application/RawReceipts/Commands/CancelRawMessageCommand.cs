using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawReceipts.Commands;

/// <summary>
/// Cancels a raw receipt message that was entered in error - the wrong customer, the
/// wrong item or the wrong warehouse (spec sections 10-11 and 49).
///
/// A raw receipt is a POSTED stock document with no draft state, so its correction is a
/// cancellation, never an edit and never a delete:
///   * the document and its original IN ledger rows are preserved,
///   * the stock it brought in is reversed with an equal-and-opposite OUT row per line,
///   * the receipt keeps who cancelled it, when and why,
///   * balances are never edited in place, and a second cancellation is refused.
///
/// Safety gate: the reversal is only unambiguous while NOTHING has consumed the material.
/// The handler therefore refuses - before touching anything - when
///   * any raw allocation already holds this receipt for a Job Order, or
///   * the lot's live ledger balance is no longer the quantity the receipt actually
///     accepted (something was issued, transferred, returned or adjusted against it).
/// In those cases the operator must first reverse the dependent document; the refusal
/// states exactly what stands in the way rather than guessing a business rule.
/// </summary>
public record CancelRawMessageCommand(Guid Id, string Reason) : IRequest<Unit>;

public class CancelRawMessageCommandValidator : AbstractValidator<CancelRawMessageCommand>
{
    public CancelRawMessageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A cancellation reason is required - it is part of the audit trail.")
            .MaximumLength(1000);
    }
}

public class CancelRawMessageCommandHandler : IRequestHandler<CancelRawMessageCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CancelRawMessageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<Unit> Handle(CancelRawMessageCommand request, CancellationToken cancellationToken)
    {
        // The reversal row is dated today (spec section 43), so only a period covering today blocks it.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var snapshot = await _db.RawMessages.AsNoTracking()
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.Id);

        if (snapshot.IsCancelled)
            throw new DomainException($"Raw receipt {snapshot.MessageNumber} is already cancelled.");

        // 1. A receipt held by a Job Order is not merely a document - it is the material a
        //    production order is going to run on. Reversing it now would leave that order
        //    pointing at stock that does not exist, so it is refused with the exact count.
        var allocations = await _db.RawAllocations.AsNoTracking()
            .Where(a => a.RawMessageId == snapshot.Id)
            .Select(a => a.ProductionOrderId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (allocations > 0)
            throw new DomainException(
                $"Raw receipt {snapshot.MessageNumber} is already allocated to {allocations} Job Order(s). " +
                "Reverse or cancel those allocations first - a receipt whose material is held by production " +
                "cannot be cancelled.");

        // The reversal posts a new OUT row, so the warehouse must still accept movements.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, snapshot.WarehouseId, cancellationToken, "to reverse its stock movements");

        // H6: the reversal and any concurrent allocation/issue share the same lot locks, so a
        // receipt can never be cancelled on a balance another session has already spent.
        var lockKeys = snapshot.Lines
            .Select(l => StockLockKey.RawLot(snapshot.WarehouseId, l.ItemId, snapshot.CustomerId, snapshot.Id))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            var message = await _db.RawMessages.Include(m => m.Lines)
                .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("RawMessage", request.Id);

            if (message.IsCancelled)
                throw new DomainException($"Raw receipt {message.MessageNumber} is already cancelled.");

            // Read under the lock, then compare each line's live balance with what the receipt
            // actually accepted. Equality means "nothing has been taken from this lot" - only
            // then is the reversal of the whole lot unambiguous.
            var reversals = new List<InventoryTransaction>();
            foreach (var line in message.Lines)
            {
                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == message.Id && t.ItemId == line.ItemId &&
                        t.CustomerId == message.CustomerId && t.WarehouseId == message.WarehouseId)
                    .Select(t => new { t.Id, t.QuantityKg, t.QuantityMeter, t.Direction, t.SourceDocumentType })
                    .ToListAsync(cancellationToken);

                var persistedIds = rows.Select(t => t.Id).ToHashSet();
                var pending = _db.InventoryTransactions.Local
                    .Where(t => t.RawMessageId == message.Id && t.ItemId == line.ItemId &&
                        t.CustomerId == message.CustomerId && t.WarehouseId == message.WarehouseId &&
                        !persistedIds.Contains(t.Id))
                    .ToList();

                var remainingKg = rows.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction)
                    + pending.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);
                var remainingMeter = rows.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction)
                    + pending.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction);

                var acceptedKg = line.AcceptedQuantityKg ?? 0m;
                var acceptedMeter = line.AcceptedQuantityMeter ?? 0m;

                if (remainingKg != acceptedKg || remainingMeter != acceptedMeter)
                {
                    var consumers = rows.Where(t => t.Direction == TransactionDirection.Out)
                        .Select(t => t.SourceDocumentType)
                        .Distinct()
                        .Where(t => t != DocumentType.RawReceiptMessage)
                        .Select(t => t.ToString())
                        .ToList();

                    throw new DomainException(
                        $"Raw receipt {message.MessageNumber} cannot be cancelled: material from it has already " +
                        $"left the lot. Remaining balance is {remainingKg} KG / {remainingMeter} M against " +
                        $"{acceptedKg} KG / {acceptedMeter} M accepted" +
                        (consumers.Count > 0 ? $" (used by: {string.Join(", ", consumers)})" : "") +
                        ". Reverse the dependent document first, then cancel the receipt.");
                }

                if (acceptedKg == 0m && acceptedMeter == 0m)
                    continue;

                // The compensating row references the receipt's own IN row for each line, so the
                // reversal is traceable to the movement it reverses (spec section 18).
                var original = rows.FirstOrDefault(t => t.Direction == TransactionDirection.In &&
                    t.SourceDocumentType == DocumentType.RawReceiptMessage);

                reversals.Add(new InventoryTransaction(
                    DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, postingDate,
                    message.WarehouseId, message.CustomerId, line.ItemId,
                    rawMessageId: message.Id, productionOrderId: null,
                    quantityKg: acceptedKg > 0m ? acceptedKg : null,
                    quantityMeter: acceptedMeter > 0m ? acceptedMeter : null,
                    direction: TransactionDirection.Out,
                    createdBy: _currentUser.UserName,
                    reversesTransactionId: original?.Id));
            }

            message.Cancel(request.Reason, _currentUser.UserName);

            foreach (var reversal in reversals)
                _db.InventoryTransactions.Add(reversal);

            _db.AuditLogEntries.Add(new AuditLogEntry(
                postingDate, _currentUser.UserName, "Cancel", "RawMessage", message.Id.ToString(),
                JsonSerializer.Serialize(new { status = RawMessageStatus.Open.ToString() }),
                JsonSerializer.Serialize(new
                {
                    status = RawMessageStatus.Cancelled.ToString(),
                    reversedLines = reversals.Count,
                    warehouseId = message.WarehouseId
                }),
                ipAddress: null,
                reason: request.Reason));

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
