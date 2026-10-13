using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.ProductionOrders.Commands;

/// <summary>
/// Allocates raw material FROM A SPECIFIC, USER-CHOSEN RawMessage AND LINE to
/// a Production Order (spec section 13). The caller must name the exact
/// RawMessageLine (ItemId) - a message may carry several item lines, and the
/// balance, the RawAllocation and the ledger row all key on (message, item),
/// so guessing a line would silently post stock against the wrong item.
/// There is deliberately no "auto-pick the oldest/cheapest/whatever line"
/// behavior anywhere in this handler or the domain layer beneath it
/// (spec section 4 - NO FIFO), and no KG↔Meter conversion. Callers may send
/// this command multiple times against the same order to allocate from
/// several messages/lines, e.g.:
///   Message 125, line 400 KG -> order
///   Message 131, line 200 KG -> order
/// each call is recorded as its own RawAllocation row.
/// </summary>
public record AllocateRawCommand(
    Guid ProductionOrderId,
    Guid RawMessageId,
    Guid ItemId,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    bool OverrideNegativeStock = false,
    string? OverrideReason = null,
    // Set only when the Approval Center dispatches this command after approving a request (spec section 24).
    Guid? ApprovalRequestId = null) : IRequest<ProductionOrderDto>, IApprovalBoundOperation
{
    // Spec section 24: the approval is bound to these exact facts - a request
    // approved for a different item, lot, account, quantity or unit cannot
    // authorise this allocation.
    public ApprovalRequestType ApprovalType => ApprovalRequestType.AllocateRaw;

    public Guid ApprovalDocumentId => ProductionOrderId;

    public string OperationSignature =>
        $"AllocateRaw|order={ProductionOrderId}|msg={RawMessageId}|item={ItemId}" +
        $"|kg={ApprovalBinding.Quantity(QuantityKg)}|m={ApprovalBinding.Quantity(QuantityMeter)}";
}

public class AllocateRawCommandValidator : AbstractValidator<AllocateRawCommand>
{
    public AllocateRawCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty()
            .WithMessage("Specify the item line being allocated - a raw message may carry several items.");
        RuleFor(x => x)
            .Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the quantity to allocate in KG and/or Meter - the user chooses this manually, it is never derived automatically.");
        RuleFor(x => x.OverrideReason)
            .NotEmpty()
            .When(x => x.OverrideNegativeStock)
            .WithMessage("A reason is required when overriding the negative-stock check.");
    }
}

public class AllocateRawCommandHandler : IRequestHandler<AllocateRawCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _allocationLock;

    public AllocateRawCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IInventoryLedgerService ledger, IDateTime clock,
        IPeriodCloseService periodClose, IAllocationLockService allocationLock)
    {
        _db = db;
        _currentUser = currentUser;
        _ledger = ledger;
        _clock = clock;
        _periodClose = periodClose;
        _allocationLock = allocationLock;
    }

    public async Task<ProductionOrderDto> Handle(AllocateRawCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: allocating raw material to production is a stock posting.
        await using var periodGuard = await _periodClose.BeginPostingAsync(_clock.UtcNow, cancellationToken);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        // No duplicate deduction (spec section 6): an order created by the canonical raw issue
        // already has its OUT movement AND its allocation row posted in the same transaction.
        // Allocating to it again would post a SECOND OUT for material the customer has already
        // been debited for. Corrections go through pause/resume, which post their own linked
        // compensating movements. Legacy orders created before the issue workflow (no issue
        // link) keep working exactly as before.
        if (order.SourceIssueTransactionId is not null)
            throw new DomainException(
                $"Job Order {order.OrderNumber} was created by issuing raw material, so its raw deduction is already posted. " +
                "Adding more here would deduct the customer's stock twice - use pause/resume to release and re-issue material.");

        // Lines are loaded explicitly (no lazy loading in this app) so the
        // item-line check below reads real rows, not an empty collection.
        var message = await _db.RawMessages
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for allocation (status: {message.Status}). Material cannot be used in production.");

        // The caller must name an item that genuinely exists on this message -
        // a multi-line message would otherwise make "first matching line" a
        // silent guess that posts stock against the wrong item.
        var line = message.Lines.FirstOrDefault(l => l.ItemId == request.ItemId)
            ?? throw new DomainException(
                $"Item '{request.ItemId}' is not a line on message '{message.MessageNumber}'. Pick one of the message's item lines.");

        // A retired warehouse never receives a new stock row: this allocation posts an OUT
        // row against the message's warehouse, so that warehouse must still be active.
        // Historical messages, their lines and their movements stay readable and unchanged.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, message.WarehouseId, cancellationToken, "to issue raw material out of it");

        // Spec sections 5-6: the allocation may only use the units this item is
        // configured for, and both measurements are required when the item asks for them.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.ItemId, request.QuantityKg, request.QuantityMeter,
            cancellationToken, "on this allocation");

        // R1: lock on the UNIFIED stock dimension. The previous call used a
        // bespoke (rawMessage, item, customer) overload whose resource string
        // did not match the one every other stock path uses, so allocation and
        // (for example) a stock adjustment on the same lot could run at the
        // same time. One key type, one resource format, one lock table.
        await using (await _allocationLock.AcquireAsync(
            StockLockKey.RawLot(message.WarehouseId, request.ItemId, order.CustomerId, message.Id), cancellationToken))
        {
            var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(
                message.Id, request.ItemId, order.CustomerId, message.WarehouseId, cancellationToken);

            await EnsureSufficientBalance(request, message, request.ItemId, order.CustomerId, balanceKg, balanceMeter, cancellationToken);

            var allocation = order.AllocateRaw(message.Id, request.ItemId, request.QuantityKg, request.QuantityMeter, _currentUser.UserName);

            _db.RawAllocations.Add(allocation);

            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, order.OrderNumber, order.Id,
                _clock.UtcNow, message.WarehouseId, order.CustomerId, request.ItemId,
                rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName));

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }

    private async Task EnsureSufficientBalance(
        AllocateRawCommand request, RawMessage message, Guid itemId, Guid customerId,
        decimal balanceKg, decimal balanceMeter, CancellationToken cancellationToken)
    {
        var kgShort = request.QuantityKg.HasValue && request.QuantityKg.Value > balanceKg;
        var meterShort = request.QuantityMeter.HasValue && request.QuantityMeter.Value > balanceMeter;

        if (!kgShort && !meterShort)
            return;

        var requested = kgShort ? request.QuantityKg!.Value : request.QuantityMeter!.Value;
        var available = kgShort ? balanceKg : balanceMeter;

        if (!request.OverrideNegativeStock)
            throw new NegativeStockException(available, requested);

        // B6: authorization goes through the permission model (HasPermission,
        // which includes the admin catch-all) - the same convention as every
        // other business check in this codebase, not raw role-string lookups.
        if (!_currentUser.HasPermission(Permissions.InventoryAllowNegativeStock) || !_currentUser.HasPermission(Permissions.InventoryApproveNegativeStock))
            throw new UnauthorizedAccessException(
                $"Overriding negative stock requires both '{Permissions.InventoryAllowNegativeStock}' and '{Permissions.InventoryApproveNegativeStock}' permissions.");

        // Spec section 24: the override is authorised by a stored, SINGLE-USE approval
        // request raised by a different user. The shared guard re-checks that it is still
        // pending, that it is about THIS order, and that the caller is not its requester,
        // then consumes it - in the same SaveChanges as the allocation, so the movement and
        // its authorisation commit together or not at all.
        var decision = await ApprovalRequestGuard.ConsumeAsync(
            _db, _currentUser, request.ApprovalRequestId, request, cancellationToken);

        // Authorized override: record it for the Negative Stock / Balance
        // Override Report (spec section 17), naming BOTH real accounts, and let the
        // allocation proceed.
        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, itemId, customerId, requested, available,
            request.OverrideReason!, decision.RequestedBy, decision.ApprovedBy));

        await Task.CompletedTask;
    }
}
