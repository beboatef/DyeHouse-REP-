using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
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
    string? OverrideReason = null) : IRequest<ProductionOrderDto>;

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
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

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

        // Serialize concurrent allocations against the same (message, item,
        // customer) key: the balance check below and the ledger write must not
        // interleave, or two simultaneous requests can both pass the check and
        // jointly oversell the balance (defeating the negative-stock rule).
        await using (await _allocationLock.AcquireAsync(message.Id, request.ItemId, order.CustomerId, cancellationToken))
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

        // Authorized override: record it for the Negative Stock / Balance
        // Override Report (spec section 17) and let the allocation proceed.
        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, itemId, customerId, requested, available,
            request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));

        await Task.CompletedTask;
    }
}
