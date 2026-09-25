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
/// Allocates raw material FROM A SPECIFIC, USER-CHOSEN RawMessage TO a
/// Production Order (spec section 13). There is deliberately no "auto-pick
/// the oldest/cheapest/whatever message" behavior anywhere in this handler
/// or the domain layer beneath it (spec section 4 - NO FIFO). Callers may
/// send this command multiple times against the same order to allocate from
/// several messages, e.g.:
///   Message 125 -> 400 KG
///   Message 131 -> 200 KG
/// each call is recorded as its own RawAllocation row.
/// </summary>
public record AllocateRawCommand(
    Guid ProductionOrderId,
    Guid RawMessageId,
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

    public AllocateRawCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IInventoryLedgerService ledger, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<ProductionOrderDto> Handle(AllocateRawCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var message = await _db.RawMessages.FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for allocation (inspection status: {message.InspectionStatus}, status: {message.Status}). Rejected or un-inspected material cannot be used in production.");

        // A message may carry several item lines; pick the line whose unit
        // matches what's being requested (kept simple for this scaffold -
        // once messages routinely carry multiple items, prefer passing an
        // explicit ItemId in the command instead of inferring it here).
        var line = message.Lines.FirstOrDefault(l =>
            (request.QuantityKg.HasValue && l.QuantityKg.HasValue) ||
            (request.QuantityMeter.HasValue && l.QuantityMeter.HasValue))
            ?? throw new DomainException("The raw message has no line matching the requested unit (KG/Meter).");

        var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(message.Id, line.ItemId, order.CustomerId, message.WarehouseId, cancellationToken);

        await EnsureSufficientBalance(request, message, line.ItemId, order.CustomerId, balanceKg, balanceMeter, cancellationToken);

        var allocation = order.AllocateRaw(message.Id, line.ItemId, request.QuantityKg, request.QuantityMeter, _currentUser.UserName);

        _db.RawAllocations.Add(allocation);

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ProductionOrder, order.OrderNumber, order.Id,
            _clock.UtcNow, message.WarehouseId, order.CustomerId, line.ItemId,
            rawMessageId: message.Id, productionOrderId: order.Id,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: TransactionDirection.Out, createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

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

        if (!_currentUser.IsInRole(Permissions.InventoryAllowNegativeStock))
            throw new UnauthorizedAccessException(
                $"Overriding negative stock requires the '{Permissions.InventoryAllowNegativeStock}' permission.");

        // Authorized override: record it for the Negative Stock / Balance
        // Override Report (spec section 17) and let the allocation proceed.
        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, itemId, customerId, requested, available,
            request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));

        await Task.CompletedTask;
    }
}
