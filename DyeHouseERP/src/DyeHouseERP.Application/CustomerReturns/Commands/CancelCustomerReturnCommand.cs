using System.Text.Json;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.CustomerReturns.DTOs;
using DyeHouseERP.Application.CustomerReturns.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerReturns.Commands;

/// <summary>
/// Cancels a customer return that was entered in error - the wrong lot, the wrong quantity or the
/// wrong customer (spec sections 32-33 + 49).
///
/// A return posts real IN ledger rows the moment it is created, so its correction is a cancellation
/// and never an edit or a delete:
///   * the document and its original IN rows are preserved,
///   * the quantity it put back on the lot is taken out again with an equal-and-opposite OUT row
///     that references the row it reverses,
///   * who cancelled it, when and why is stored on the document and in the audit trail,
///   * a second cancellation is refused.
///
/// Safety gate - the same one the raw receipt cancellation uses: the reversal must never drive a lot
/// negative, so the live balance of every affected lot is read UNDER the lot lock and compared with
/// the quantity being reversed. If the returned material has meanwhile been allocated or issued, the
/// cancellation is refused with the exact figures and the operator reverses the dependent document
/// first - no balance is ever edited and no negative stock is invented.
/// </summary>
public record CancelCustomerReturnCommand(Guid Id, string Reason) : IRequest<CustomerReturnDto>;

public class CancelCustomerReturnCommandValidator : AbstractValidator<CancelCustomerReturnCommand>
{
    public CancelCustomerReturnCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A cancellation reason is required - it is part of the audit trail.")
            .MaximumLength(1000);
    }
}

public class CancelCustomerReturnCommandHandler : IRequestHandler<CancelCustomerReturnCommand, CustomerReturnDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CancelCustomerReturnCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<CustomerReturnDto> Handle(CancelCustomerReturnCommand request, CancellationToken cancellationToken)
    {
        // The reversal row is dated today (spec section 43), so only a period covering today blocks it.
        var postingDate = _clock.UtcNow;
        await using var periodGuard = await _periodClose.BeginPostingAsync(postingDate, cancellationToken);

        var snapshot = await _db.CustomerReturns.AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("CustomerReturn", request.Id);

        if (snapshot.IsCancelled)
            throw new DomainException($"Customer return {snapshot.ReturnNumber} is already cancelled.");

        // Each line names the raw lot it landed in; that lot's warehouse has to accept the new OUT row.
        var lotIds = snapshot.Lines.Select(l => l.RawMessageId).Distinct().ToList();
        var lots = await _db.RawMessages.AsNoTracking()
            .Where(m => lotIds.Contains(m.Id))
            .Select(m => new { m.Id, m.WarehouseId, m.MessageNumber })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var warehouseId in lots.Values.Select(l => l.WarehouseId).Distinct())
            await WarehouseGuard.EnsureUsableForNewMovementAsync(
                _db, warehouseId, cancellationToken, "to reverse the return's stock movements");

        // The reversal and any concurrent allocation/issue share the same lot locks, so a return can
        // never be cancelled against a balance another session has already spent.
        var lockKeys = snapshot.Lines
            .Where(l => lots.ContainsKey(l.RawMessageId))
            .Select(l => StockLockKey.RawLot(lots[l.RawMessageId].WarehouseId, l.ItemId, snapshot.CustomerId, l.RawMessageId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            var customerReturn = await _db.CustomerReturns.Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("CustomerReturn", request.Id);

            if (customerReturn.IsCancelled)
                throw new DomainException($"Customer return {customerReturn.ReturnNumber} is already cancelled.");

            var reversals = new List<InventoryTransaction>();
            foreach (var line in customerReturn.Lines)
            {
                if (!lots.TryGetValue(line.RawMessageId, out var lot))
                    throw new NotFoundException("RawMessage", line.RawMessageId);

                var rows = await _db.InventoryTransactions.AsNoTracking()
                    .Where(t => t.RawMessageId == line.RawMessageId && t.ItemId == line.ItemId &&
                        t.CustomerId == customerReturn.CustomerId && t.WarehouseId == lot.WarehouseId)
                    .Select(t => new { t.Id, t.QuantityKg, t.QuantityMeter, t.Direction, t.SourceDocumentId })
                    .ToListAsync(cancellationToken);

                var persistedIds = rows.Select(t => t.Id).ToHashSet();
                var pending = _db.InventoryTransactions.Local
                    .Where(t => t.RawMessageId == line.RawMessageId && t.ItemId == line.ItemId &&
                        t.CustomerId == customerReturn.CustomerId && t.WarehouseId == lot.WarehouseId &&
                        !persistedIds.Contains(t.Id))
                    .ToList();

                var balanceKg = rows.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction)
                    + pending.Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction);
                var balanceMeter = rows.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction)
                    + pending.Sum(t => (t.QuantityMeter ?? 0m) * (int)t.Direction);

                var backKg = line.QuantityKg ?? 0m;
                var backMeter = line.QuantityMeter ?? 0m;

                // The returned stock must still be there in full: reversing material that has already
                // been allocated or used would either invent negative stock or quietly take it from
                // another job's material.
                if (backKg > balanceKg || backMeter > balanceMeter)
                    throw new DomainException(
                        $"Customer return {customerReturn.ReturnNumber} cannot be cancelled: the material it " +
                        $"returned to lot {lot.MessageNumber} has already been used. Lot balance is " +
                        $"{balanceKg} KG / {balanceMeter} M against {backKg} KG / {backMeter} M to reverse. " +
                        "Reverse the dependent document first, then cancel the return.");

                // The IN row this return itself posted is the row being reversed.
                var original = rows.FirstOrDefault(t => t.Direction == TransactionDirection.In &&
                    t.SourceDocumentId == customerReturn.Id);

                reversals.Add(new InventoryTransaction(
                    DocumentType.CustomerReturn, customerReturn.ReturnNumber, customerReturn.Id, postingDate,
                    lot.WarehouseId, customerReturn.CustomerId, line.ItemId,
                    rawMessageId: line.RawMessageId, productionOrderId: line.ProductionOrderId,
                    quantityKg: backKg > 0m ? backKg : null,
                    quantityMeter: backMeter > 0m ? backMeter : null,
                    direction: TransactionDirection.Out,
                    createdBy: _currentUser.UserName,
                    reversesTransactionId: original?.Id));
            }

            customerReturn.Cancel(request.Reason, _currentUser.UserName);

            foreach (var reversal in reversals)
                _db.InventoryTransactions.Add(reversal);

            _db.AuditLogEntries.Add(new AuditLogEntry(
                postingDate, _currentUser.UserName, "Cancel", "CustomerReturn", customerReturn.Id.ToString(),
                JsonSerializer.Serialize(new { isCancelled = false }),
                JsonSerializer.Serialize(new
                {
                    isCancelled = true,
                    reversedLines = reversals.Count,
                    quantityKg = reversals.Sum(r => r.QuantityKg ?? 0m),
                    quantityMeter = reversals.Sum(r => r.QuantityMeter ?? 0m)
                }),
                ipAddress: null,
                reason: request.Reason));

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetCustomerReturnsQueryHandler.LoadDtoAsync(_db, request.Id, cancellationToken);
    }
}
