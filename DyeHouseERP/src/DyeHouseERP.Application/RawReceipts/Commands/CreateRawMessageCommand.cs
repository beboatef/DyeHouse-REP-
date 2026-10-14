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
/// Receiving a customer's raw material (spec sections 8-11): one receipt = one customer, one
/// warehouse, one date and N item lines, each with its own KG and/or Meter quantity, optional
/// bale/roll count, optional colour and notes. The receipt number is always system-generated,
/// it posts exactly one IN ledger row per measured unit, and inspection is NOT part of it -
/// a receipt is complete and allocatable the moment it is posted.
///
/// <see cref="RequestId"/> is the client idempotency token: a retried (or double-submitted)
/// confirmation carrying the same token returns the receipt that was already posted instead
/// of posting the physical delivery twice (spec section 5 - "once, idempotent against
/// retries"). A unique filtered index on RawMessages.ReceiveRequestId backs the check up at
/// the database level, so two racing identical requests still produce one receipt and one IN
/// movement per unit.
/// </summary>
public record CreateRawMessageCommand(
    DateTime ReceiptDate,
    Guid CustomerId,
    Guid WarehouseId,
    string? Notes,
    List<RawMessageLineInput> Lines,
    Guid? RequestId = null) : IRequest<RawMessageDto>;

public class CreateRawMessageCommandValidator : AbstractValidator<CreateRawMessageCommand>
{
    public CreateRawMessageCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A raw receipt message must contain at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).NotEmpty();
            line.RuleFor(l => l.Color).MaximumLength(100);
            line.RuleFor(l => l)
                .Must(l => l.QuantityKg is > 0 || l.QuantityMeter is > 0)
                .WithMessage("Each line needs a KG quantity and/or a Meter quantity greater than zero.");
        });
    }
}

public class CreateRawMessageCommandHandler : IRequestHandler<CreateRawMessageCommand, RawMessageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;
    private readonly IInventoryLedgerService _ledger;

    public CreateRawMessageCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator,
        IDateTime clock,
        IPeriodCloseService periodClose,
        IAllocationLockService stockLock,
        IInventoryLedgerService ledger)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _clock = clock;
        _periodClose = periodClose;
        _stockLock = stockLock;
        _ledger = ledger;
    }

    public async Task<RawMessageDto> Handle(CreateRawMessageCommand request, CancellationToken cancellationToken)
    {
        // ---- Idempotency (spec section 5) ------------------------------------
        // A confirmation retried with the same token returns the SAME receipt and posts
        // nothing - the physical delivery must never be written into stock twice. The
        // unique index on ReceiveRequestId is the database backstop for a true race
        // between two identical requests. Checking this BEFORE the period guard means a
        // retry of an already-posted receipt still succeeds after the period closed.
        if (request.RequestId.HasValue)
        {
            var postedId = await _db.RawMessages.AsNoTracking()
                .Where(m => m.ReceiveRequestId == request.RequestId.Value)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (postedId.HasValue)
                return await LoadDtoAsync(postedId.Value, cancellationToken);
        }

        // Spec section 43: a raw receipt is a stock posting, so its date must
        // fall outside any closed period.
        await using var periodGuard = await _periodClose.BeginPostingAsync(request.ReceiptDate, cancellationToken);

        // A receipt is written into exactly the warehouse the caller picked, so the
        // warehouse must exist and still be active. Deactivated warehouses stay readable
        // for every message already received into them.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, request.WarehouseId, cancellationToken, "to receive raw material into it");

        // Spec section 5: each line may only use the units its item is configured for,
        // and when the item requires both units, BOTH measurements must be recorded.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db,
            request.Lines.Select(l => (l.ItemId, l.QuantityKg, l.QuantityMeter)),
            cancellationToken, "on this raw receipt");

        // Section 6: the message number is ALWAYS system-generated via the
        // concurrency-safe sequence engine - never typed by the user.
        var messageNumber = await _numberGenerator.NextAsync(DocumentType.RawReceiptMessage, cancellationToken: cancellationToken);

        var message = new RawMessage(
            messageNumber, request.ReceiptDate, request.CustomerId, request.WarehouseId,
            _currentUser.UserName, _currentUser.UserName, request.Notes, request.RequestId);

        // H6: receipts are stock postings like every other movement, so the IN
        // rows commit under the same per-lot locks the outbound paths take. A
        // receipt and a concurrent allocation of the same customer/lot can no
        // longer interleave between their reads and their writes.
        var lockKeys = request.Lines
            .Select(l => StockLockKey.RawLot(request.WarehouseId, l.ItemId, request.CustomerId, message.Id))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {

        foreach (var line in request.Lines)
            message.AddLine(line.ItemId, line.QuantityKg, line.QuantityMeter, line.PieceCount, line.Notes, line.Color);

        _db.RawMessages.Add(message);

        // Post the corresponding inventory ledger rows (spec section 18) -
        // one IN transaction per line, per unit that was actually recorded.
        foreach (var line in message.Lines)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.RawReceiptMessage, message.MessageNumber, message.Id,
                request.ReceiptDate, request.WarehouseId, request.CustomerId, line.ItemId,
                rawMessageId: message.Id, productionOrderId: null,
                quantityKg: line.QuantityKg, quantityMeter: line.QuantityMeter,
                direction: TransactionDirection.In, createdBy: _currentUser.UserName));
        }

        await _db.SaveChangesAsync(cancellationToken);
        }

        return await LoadDtoAsync(message.Id, cancellationToken);
    }

    /// <summary>
    /// The single receipt DTO projection, shared by the normal create path and the
    /// idempotent retry path so both answer with exactly the same shape: the receipt, its
    /// lines, and each line's balance read LIVE from the ledger (never from a stored
    /// balance column - spec section 18). A retry therefore reports what the lot actually
    /// holds right now, even if material has been issued from it since the first call.
    /// </summary>
    private async Task<RawMessageDto> LoadDtoAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _db.RawMessages.AsNoTracking()
            .Include(m => m.Lines)
            .FirstAsync(m => m.Id == messageId, cancellationToken);

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == message.CustomerId, cancellationToken);
        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == message.WarehouseId, cancellationToken);

        var itemIds = message.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var lines = new List<RawMessageLineDto>();
        foreach (var line in message.Lines)
        {
            var (remainingKg, remainingMeter) = await _ledger.GetCustomerBalanceAsync(
                message.Id, line.ItemId, message.CustomerId, message.WarehouseId, cancellationToken);

            lines.Add(new RawMessageLineDto
            {
                Id = line.Id,
                ItemId = line.ItemId,
                ItemCode = items.GetValueOrDefault(line.ItemId)?.Code ?? string.Empty,
                ItemName = items.GetValueOrDefault(line.ItemId)?.Name ?? string.Empty,
                QuantityKg = line.QuantityKg,
                QuantityMeter = line.QuantityMeter,
                PieceCount = line.PieceCount,
                Color = line.Color,
                Notes = line.Notes,
                RejectedQuantityKg = line.RejectedQuantityKg,
                RejectedQuantityMeter = line.RejectedQuantityMeter,
                AcceptedQuantityKg = line.AcceptedQuantityKg,
                AcceptedQuantityMeter = line.AcceptedQuantityMeter,
                RemainingKg = line.QuantityKg.HasValue ? remainingKg : null,
                RemainingMeter = line.QuantityMeter.HasValue ? remainingMeter : null
            });
        }

        return new RawMessageDto
        {
            Id = message.Id,
            MessageNumber = message.MessageNumber,
            ReceiptDate = message.ReceiptDate,
            CustomerId = message.CustomerId,
            CustomerCode = customer?.Code ?? string.Empty,
            CustomerName = customer?.Name ?? string.Empty,
            WarehouseId = message.WarehouseId,
            WarehouseName = warehouse?.Name ?? string.Empty,
            ReceivingUser = message.ReceivingUser,
            Notes = message.Notes,
            InspectionStatus = message.InspectionStatus,
            Status = message.Status,
            HasRejections = message.HasRejections,
            CancellationReason = message.CancellationReason,
            CancelledBy = message.CancelledBy,
            CancelledAtUtc = message.CancelledAtUtc,
            Lines = lines
        };
    }
}
