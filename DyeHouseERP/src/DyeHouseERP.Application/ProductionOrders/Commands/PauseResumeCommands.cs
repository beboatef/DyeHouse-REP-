using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Application.Common.Interfaces;
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
// Pause / Resume (spec section 19)
// =====================================================================

/// <summary>
/// Pauses a Job Order (موقوف مؤقتًا) and releases the user's chosen share of the
/// UNUSED raw material back to the customer's own raw stock.
///
/// The rules, as agreed:
///   * "unused" = allocated quantity minus the quantity already consumed, where
///     consumed means material that has actually entered a COMPLETED processing
///     step (the recorded stage input). Material still sitting in the process is
///     not releasable, and neither is anything already issued in a past cycle.
///   * the release quantity is USER-ENTERED and bounded by that un-consumed
///     figure - the server refuses anything larger rather than silently clamping it.
///   * the release is posted as NEW compensating IN movements against the very
///     lots this order was allocated from. The original allocation and its OUT
///     movement are left untouched, so the ledger keeps both cycles.
/// </summary>
public record PauseProductionOrderCommand(
    Guid ProductionOrderId,
    decimal? ReleaseKg,
    decimal? ReleaseMeter,
    string Reason) : IRequest<ProductionOrderDto>;

public class PauseProductionOrderCommandValidator : AbstractValidator<PauseProductionOrderCommand>
{
    public PauseProductionOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Pausing a production order requires a reason.");
        RuleFor(x => x)
            .Must(x => x.ReleaseKg is > 0 || x.ReleaseMeter is > 0)
            .WithMessage("Enter the quantity of unused raw material to release back to the customer.");
        RuleFor(x => x.ReleaseKg).GreaterThanOrEqualTo(0).When(x => x.ReleaseKg.HasValue);
        RuleFor(x => x.ReleaseMeter).GreaterThanOrEqualTo(0).When(x => x.ReleaseMeter.HasValue);
    }
}

public class PauseProductionOrderCommandHandler : IRequestHandler<PauseProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _allocationLock;

    public PauseProductionOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService allocationLock)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _allocationLock = allocationLock;
    }

    public async Task<ProductionOrderDto> Handle(PauseProductionOrderCommand request, CancellationToken cancellationToken)
    {
        // The release is a stock posting, so a closed period blocks it.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var allocations = await _db.RawAllocations
            .Where(a => a.ProductionOrderId == order.Id)
            .OrderBy(a => a.AllocatedAtUtc)
            .ToListAsync(cancellationToken);

        var allocatedKg = allocations.Sum(a => a.QuantityKg ?? 0);
        var allocatedMeter = allocations.Sum(a => a.QuantityMeter ?? 0);

        // Consumed = material that has already entered a completed processing step.
        // Anything else is still unused and can legitimately go back to the customer.
        var completedStages = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => s.ProductionOrderId == order.Id && s.Status == StageExecutionStatus.Completed)
            .ToListAsync(cancellationToken);

        var consumedKg = completedStages.Sum(s => s.InputKg ?? 0);
        var consumedMeter = completedStages.Sum(s => s.InputMeter ?? 0);

        var unConsumedKg = allocatedKg - consumedKg;
        var unConsumedMeter = allocatedMeter - consumedMeter;

        // Bounded, not clamped: a release larger than the unused balance is a
        // mistake the user needs to see, not something to quietly round off.
        if (request.ReleaseKg is > 0 && request.ReleaseKg > unConsumedKg)
            throw new DomainException(
                $"Cannot release {request.ReleaseKg} KG: only {unConsumedKg} KG of the allocated {allocatedKg} KG is still unused (already consumed by production: {consumedKg} KG).");
        if (request.ReleaseMeter is > 0 && request.ReleaseMeter > unConsumedMeter)
            throw new DomainException(
                $"Cannot release {request.ReleaseMeter} Meter: only {unConsumedMeter} Meter of the allocated {allocatedMeter} Meter is still unused (already consumed by production: {consumedMeter} Meter).");

        var messages = await _db.RawMessages
            .Where(m => allocations.Select(a => a.RawMessageId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        var lockKeys = allocations
            .Select(a => StockLockKey.RawLot(messages[a.RawMessageId].WarehouseId, a.ItemId, order.CustomerId, a.RawMessageId))
            .Distinct()
            .ToList();

        await using (await _allocationLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            // The released material goes back to the lots THIS order was allocated
            // from, newest allocation first so the movement ties to the most
            // recent issue. That is bookkeeping inside one order - it is not an
            // automatic pick between different stock sources.
            var remainingKg = request.ReleaseKg ?? 0;
            var remainingMeter = request.ReleaseMeter ?? 0;

            foreach (var allocation in allocations.AsEnumerable().Reverse())
            {
                if (remainingKg <= 0 && remainingMeter <= 0) break;

                var takeKg = Math.Min(remainingKg, allocation.QuantityKg ?? 0);
                var takeMeter = Math.Min(remainingMeter, allocation.QuantityMeter ?? 0);
                if (takeKg <= 0 && takeMeter <= 0) continue;

                var message = messages[allocation.RawMessageId];

                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.ProductionOrderPause, order.OrderNumber, order.Id,
                    _clock.UtcNow, message.WarehouseId, order.CustomerId, allocation.ItemId,
                    rawMessageId: allocation.RawMessageId, productionOrderId: order.Id,
                    quantityKg: takeKg > 0 ? takeKg : null,
                    quantityMeter: takeMeter > 0 ? takeMeter : null,
                    direction: TransactionDirection.In, createdBy: _currentUser.UserName));

                remainingKg -= takeKg;
                remainingMeter -= takeMeter;
            }

            order.Pause(request.Reason, _currentUser.UserName);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
    }
}

/// <summary>
/// Resumes a paused Job Order (spec section 19).
///
/// The SAME order continues - no new Job Order is created and nothing from the
/// paused cycle is deleted. Material is re-issued as a NEW allocation and a NEW
/// movement (the user names the source lot explicitly, as for any allocation).
/// The previous cycle's loss percentage is reported back as the expected-output
/// basis for this cycle; the user still enters the ACTUAL output, and the actual
/// loss is recalculated from it.
/// </summary>
public record ResumeProductionOrderCommand(
    Guid ProductionOrderId,
    Guid RawMessageId,
    Guid ItemId,
    decimal? QuantityKg,
    decimal? QuantityMeter,
    bool OverrideNegativeStock = false,
    string? OverrideReason = null) : IRequest<ProductionOrderDto>;

public class ResumeProductionOrderCommandValidator : AbstractValidator<ResumeProductionOrderCommand>
{
    public ResumeProductionOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty()
            .WithMessage("Specify the item line being issued - a raw message may carry several items.");
        RuleFor(x => x)
            .Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Enter the quantity to issue for the resumed cycle.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.OverrideNegativeStock)
            .WithMessage("A reason is required when overriding the negative-stock check.");
    }
}

public class ResumeProductionOrderCommandHandler : IRequestHandler<ResumeProductionOrderCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _allocationLock;

    public ResumeProductionOrderCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IInventoryLedgerService ledger, IDateTime clock, IPeriodCloseService periodClose,
        IAllocationLockService allocationLock)
    {
        _db = db; _currentUser = currentUser; _ledger = ledger; _clock = clock;
        _periodClose = periodClose; _allocationLock = allocationLock;
    }

    public async Task<ProductionOrderDto> Handle(ResumeProductionOrderCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var order = await _db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == request.ProductionOrderId, cancellationToken)
            ?? throw new NotFoundException("ProductionOrder", request.ProductionOrderId);

        var message = await _db.RawMessages
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for allocation (status: {message.Status}).");

        if (!message.Lines.Any(l => l.ItemId == request.ItemId))
            throw new DomainException(
                $"Item '{request.ItemId}' is not a line on message '{message.MessageNumber}'. Pick one of the message's item lines.");

        await using (await _allocationLock.AcquireAsync(
            StockLockKey.RawLot(message.WarehouseId, request.ItemId, order.CustomerId, message.Id), cancellationToken))
        {
            var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(
                message.Id, request.ItemId, order.CustomerId, message.WarehouseId, cancellationToken);

            await EnsureSufficientBalanceAsync(request, message, order, balanceKg, balanceMeter, cancellationToken);

            // The allocation itself goes through the ordinary path, so a resumed
            // cycle produces exactly the same ledger shape as a first cycle.
            var allocation = order.AllocateRaw(
                message.Id, request.ItemId, request.QuantityKg, request.QuantityMeter, _currentUser.UserName);

            _db.RawAllocations.Add(allocation);

            _db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, order.OrderNumber, order.Id,
                _clock.UtcNow, message.WarehouseId, order.CustomerId, request.ItemId,
                rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName));

            // A paused order has no active stage row, so AllocateRaw's Draft
            // transition cannot apply here - Resume() sets it straight back to
            // InProduction and the job continues in التشكيل.
            order.Resume("Resumed with a new raw material issue", _currentUser.UserName);

            await _db.SaveChangesAsync(cancellationToken);
        }

        var dto = await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
        await ApplyExpectedOutputBasisAsync(dto, cancellationToken);
        return dto;
    }

    /// <summary>
    /// Attaches the previous cycle's loss percentage and the expected output it
    /// implies to the resumed order (spec section 19). This is advisory: the user
    /// still types the actual output, and the actual loss is recalculated from it.
    /// </summary>
    private async Task ApplyExpectedOutputBasisAsync(ProductionOrderDto dto, CancellationToken cancellationToken)
    {
        var lastCompleted = await _db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(s => s.ProductionOrderId == dto.Id && s.Status == StageExecutionStatus.Completed)
            .OrderByDescending(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastCompleted is null) return;

        // Loss % against that stage's own baseline. Baseline = its recorded input
        // when there is one, otherwise its starting quantity, so the ratio is
        // always "of what went in", never of a hard-coded figure.
        var baselineKg = lastCompleted.InputKg ?? lastCompleted.OutputKg;
        var baselineMeter = lastCompleted.InputMeter ?? lastCompleted.OutputMeter;

        if (baselineKg is > 0 && lastCompleted.LossKg is not null)
        {
            dto.PreviousCycleLossPercent = Math.Round((decimal)lastCompleted.LossKg / baselineKg.Value * 100, 2);
            dto.ExpectedOutputKg = lastCompleted.OutputKg;
        }
        else if (baselineMeter is > 0 && lastCompleted.LossMeter is not null)
        {
            dto.PreviousCycleLossPercent = Math.Round((decimal)lastCompleted.LossMeter / baselineMeter.Value * 100, 2);
            dto.ExpectedOutputMeter = lastCompleted.OutputMeter;
        }
    }

    private async Task EnsureSufficientBalanceAsync(
        ResumeProductionOrderCommand request, RawMessage message, ProductionOrder order,
        decimal balanceKg, decimal balanceMeter, CancellationToken cancellationToken)
    {
        var kgShort = request.QuantityKg.HasValue && request.QuantityKg.Value > balanceKg;
        var meterShort = request.QuantityMeter.HasValue && request.QuantityMeter.Value > balanceMeter;
        if (!kgShort && !meterShort) return;

        var requested = kgShort ? request.QuantityKg!.Value : request.QuantityMeter!.Value;
        var available = kgShort ? balanceKg : balanceMeter;

        if (!request.OverrideNegativeStock)
            throw new NegativeStockException(available, requested);

        if (!_currentUser.HasPermission(Permissions.InventoryAllowNegativeStock) || !_currentUser.HasPermission(Permissions.InventoryApproveNegativeStock))
            throw new UnauthorizedAccessException(
                $"Overriding negative stock requires both '{Permissions.InventoryAllowNegativeStock}' and '{Permissions.InventoryApproveNegativeStock}' permissions.");

        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, request.ItemId, order.CustomerId, requested, available,
            request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));

        await Task.CompletedTask;
    }
}