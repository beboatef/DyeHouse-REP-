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
/// Cancels a posted ready-goods transfer instead of physically deleting it.
/// The transfer created InventoryTransaction rows when it was posted, and the
/// ledger is append-only (spec section 18): deleting the document would leave
/// its IN rows orphaned and the ready balance permanently wrong. Cancelling
/// posts one equal-and-opposite reversal row per original row (same pattern
/// as CancelDeliveryCommand) keyed by ReversesTransactionId, so the balance
/// stays correct and every correction stays traceable. The unique index on
/// ProductionOrderId stays satisfied because the row is never removed.
/// Ordering: lock first, re-read live status, THEN the permission/delivery-lock
/// guard, so a delivery committing concurrently is still caught.
/// </summary>
public sealed record DeleteReadyGoodsTransferCommand(Guid Id, string Reason) : IRequest<ReadyGoodsTransferDto>;

public class DeleteReadyGoodsTransferCommandValidator : AbstractValidator<DeleteReadyGoodsTransferCommand>
{
    public DeleteReadyGoodsTransferCommandValidator() => RuleFor(x => x.Reason).NotEmpty();
}

public sealed class DeleteReadyGoodsTransferCommandHandler
    : IRequestHandler<DeleteReadyGoodsTransferCommand, ReadyGoodsTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryMovementPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public DeleteReadyGoodsTransferCommandHandler(
        IApplicationDbContext db,
        IInventoryMovementPermissionService permissionService,
        ICurrentUserService currentUser,
        IDateTime clock,
        IPeriodCloseService periodClose,
        IAllocationLockService stockLock)
    {
        _db = db;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _clock = clock;
        _periodClose = periodClose;
        _stockLock = stockLock;
    }

    public async Task<ReadyGoodsTransferDto> Handle(
        DeleteReadyGoodsTransferCommand request,
        CancellationToken cancellationToken)
    {
        // The reversal rows are dated today, so a closed period only blocks
        // the cancellation if today itself is closed - the same convention
        // used by CancelDeliveryCommandHandler (spec section 43).
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var transferExists = await _db.ReadyGoodsTransfers.AnyAsync(x => x.Id == request.Id, cancellationToken);
        if (!transferExists)
            throw new NotFoundException("ReadyGoodsTransfer", request.Id);

        // Exactly the rows this transfer created when it posted. Keyed on the
        // source-document identity rather than the warehouse dimension so the
        // reversal is complete and only covers this transfer's own rows.
        var originalRows = await _db.InventoryTransactions
            .Where(t => t.SourceDocumentId == request.Id && t.SourceDocumentType == DocumentType.ReadyGoodsTransfer)
            .ToListAsync(cancellationToken);

        // H6/R2: the reversal credits the same ready lot the transfer debited,
        // locked with the same key shape the delivery path uses, so a
        // cancellation cannot interleave with a delivery of the same order.
        // The permission/delivery-lock guard runs INSIDE the lock: if it ran
        // before, a delivery could commit between the check and the reversal
        // and the transfer would be cancelled against stock that was already
        // delivered.
        var lockKeys = originalRows
            .Select(r => StockLockKey.ReadyLot(r.ItemId, r.ProductionOrderId ?? Guid.Empty))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
        // R3: re-read the document INSIDE the lock and re-check its status.
        // Another request may have cancelled it while this one was queueing for
        // the lock; without this re-check both would post reversal rows and the
        // ready lot would be credited twice. The RowVersion token then makes a
        // genuine simultaneous update fail at the database too.
        var transfer = await _db.ReadyGoodsTransfers
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("ReadyGoodsTransfer", request.Id);

        if (transfer.Status == ReadyGoodsTransferStatus.Cancelled)
            throw new DomainException(
                $"Ready goods transfer '{transfer.TransferNumber}' is already cancelled and cannot be cancelled again.");

        // Permission + delivery/invoice lock guard - re-evaluated under the
        // stock lock so a Delivered delivery that commits concurrently is seen.
        _permissionService.EnsureCanDelete(transfer.ProductionOrderId);

        foreach (var row in originalRows)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, _clock.UtcNow,
                row.WarehouseId, row.CustomerId, row.ItemId, row.RawMessageId, row.ProductionOrderId,
                row.QuantityKg, row.QuantityMeter,
                direction: row.Direction == TransactionDirection.Out ? TransactionDirection.In : TransactionDirection.Out,
                createdBy: _currentUser.UserName, reversesTransactionId: row.Id));
        }

        transfer.Cancel(request.Reason, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        }

        return await LoadDtoAsync(_db, request.Id, cancellationToken);
    }

    internal static async Task<ReadyGoodsTransferDto> LoadDtoAsync(
        IApplicationDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var transfer = await db.ReadyGoodsTransfers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("ReadyGoodsTransfer", id);

        var order = await db.ProductionOrders.AsNoTracking()
            .FirstAsync(o => o.Id == transfer.ProductionOrderId, cancellationToken);
        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == transfer.CustomerId, cancellationToken);
        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == transfer.ItemId, cancellationToken);

        return new ReadyGoodsTransferDto
        {
            Id = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            ProductionOrderId = transfer.ProductionOrderId,
            ProductionOrderNumber = order.OrderNumber,
            CustomerId = transfer.CustomerId,
            CustomerCode = customer?.Code ?? "",
            ItemId = transfer.ItemId,
            ItemCode = item?.Code ?? "",
            Color = order.Color,
            QuantityKg = transfer.QuantityKg,
            QuantityMeter = transfer.QuantityMeter,
            PieceCount = transfer.PieceCount,
            Status = transfer.Status.ToString()
        };
    }
}
