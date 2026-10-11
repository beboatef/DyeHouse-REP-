using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Domain.Common;
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
// Pause / Resume (spec section 19)
// =====================================================================

/// <summary>
/// Pauses a Job Order (موقوف مؤقتًا) and releases the user's chosen share of the
/// UNUSED raw material back to the customer's own raw stock.
///
/// The rules, as agreed:
///   * "unused" is read from the INVENTORY LEDGER: what this order was issued
///     from a raw lot (its OUT movements) minus what it has already returned
///     (its pause-reversal IN movements). Material that was already processed is
///     therefore still releasable, and a released quantity can never be credited twice.
///   * the release quantity is USER-ENTERED and bounded by that outstanding
///     figure - the server refuses anything larger rather than silently clamping it.
///   * the release is posted as a NEW compensating IN movement against the exact
///     lot the user names. With several source lots the system never picks one
///     itself (no FIFO). The original allocations and OUT movements are left
///     untouched, so the ledger keeps every cycle.
///   * a release of zero is legitimate: pausing with nothing going back to stock.
/// </summary>
public record PauseProductionOrderCommand(
    Guid ProductionOrderId,
    string Reason,
    Guid? RawMessageId = null,
    Guid? ItemId = null,
    decimal? ReleaseKg = null,
    decimal? ReleaseMeter = null) : IRequest<ProductionOrderDto>;

public class PauseProductionOrderCommandValidator : AbstractValidator<PauseProductionOrderCommand>
{
    public PauseProductionOrderCommandValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Pausing a production order requires a reason.");
        // Zero is explicitly allowed: pausing with nothing going back to stock.
        RuleFor(x => x.ReleaseKg).GreaterThanOrEqualTo(0).When(x => x.ReleaseKg.HasValue)
            .WithMessage("A negative return is never valid.");
        RuleFor(x => x.ReleaseMeter).GreaterThanOrEqualTo(0).When(x => x.ReleaseMeter.HasValue)
            .WithMessage("A negative return is never valid.");
    }
}

/// <summary>One raw lot as the ledger shows it for an order: issued minus already returned.</summary>
internal sealed record LedgerLot(
    Guid RawMessageId, Guid ItemId, Guid WarehouseId, decimal OutstandingKg, decimal OutstandingMeter);

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

        var releaseKg = request.ReleaseKg ?? 0;
        var releaseMeter = request.ReleaseMeter ?? 0;

        // The releasable figure comes from the LEDGER - the order's own issue (OUT)
        // movements netted against its pause reversals (IN) - never from a stage's
        // InputKg. A lot's outstanding claim is therefore independent of how far the
        // material has been processed, and can never be returned twice.
        var ledger = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductionOrderId == order.Id && t.RawMessageId != null
                && (t.SourceDocumentType == DocumentType.ProductionOrder
                    || t.SourceDocumentType == DocumentType.ProductionOrderPause))
            .Select(t => new
            {
                t.SourceDocumentType, t.RawMessageId, t.ItemId, t.WarehouseId, t.QuantityKg, t.QuantityMeter
            })
            .ToListAsync(cancellationToken);

        var lots = new Dictionary<(Guid LotId, Guid ItemId), (Guid WarehouseId, decimal IssuedKg, decimal IssuedMeter, decimal ReturnedKg, decimal ReturnedMeter)>();
        foreach (var row in ledger)
        {
            var key = (row.RawMessageId!.Value, row.ItemId);
            lots.TryGetValue(key, out var acc);
            if (row.SourceDocumentType == DocumentType.ProductionOrder)
                lots[key] = (row.WarehouseId, acc.IssuedKg + (row.QuantityKg ?? 0),
                    acc.IssuedMeter + (row.QuantityMeter ?? 0), acc.ReturnedKg, acc.ReturnedMeter);
            else
                lots[key] = (row.WarehouseId, acc.IssuedKg, acc.IssuedMeter,
                    acc.ReturnedKg + (row.QuantityKg ?? 0), acc.ReturnedMeter + (row.QuantityMeter ?? 0));
        }

        var releasable = lots
            .Select(kv => new LedgerLot(kv.Key.LotId, kv.Key.ItemId, kv.Value.WarehouseId,
                kv.Value.IssuedKg - kv.Value.ReturnedKg, kv.Value.IssuedMeter - kv.Value.ReturnedMeter))
            .Where(l => l.OutstandingKg > 0 || l.OutstandingMeter > 0)
            .ToList();

        if (releaseKg > 0 || releaseMeter > 0)
        {
            // Which lot the material goes back to is the USER's decision, never an
            // automatic pick: with several source lots the request is refused rather
            // than silently guessed.
            LedgerLot chosen;
            if (request.RawMessageId.HasValue)
            {
                chosen = releasable.FirstOrDefault(l => l.RawMessageId == request.RawMessageId.Value
                        && (request.ItemId is null || l.ItemId == request.ItemId.Value))
                    ?? throw new DomainException(
                        $"Raw material message ({request.RawMessageId}) is not one this Job Order was issued material from.");
            }
            else
            {
                if (releasable.Count > 1)
                    throw new DomainException(
                        $"Job Order {order.OrderNumber} was issued material from more than one raw message. Name the raw message the unused material is returned to - the system never selects a lot on its own.");

                chosen = releasable.SingleOrDefault()
                    ?? throw new DomainException(
                        $"Job Order {order.OrderNumber} has no outstanding raw material to return.");
            }

            // Bounded, not clamped: a release larger than what the chosen lot still
            // holds for this order is a mistake the user needs to see.
            if (releaseKg > chosen.OutstandingKg)
                throw new DomainException(
                    $"Cannot release {releaseKg} KG from raw message {chosen.RawMessageId}: only {chosen.OutstandingKg} KG of what this Job Order took from that lot is still outstanding.");
            if (releaseMeter > chosen.OutstandingMeter)
                throw new DomainException(
                    $"Cannot release {releaseMeter} Meter from raw message {chosen.RawMessageId}: only {chosen.OutstandingMeter} Meter of what this Job Order took from that lot is still outstanding.");

            // The state change happens first, so a refused pause never leaves a
            // half-written movement behind in the change tracker.
            order.Pause(request.Reason, _currentUser.UserName);

            await using (await _allocationLock.AcquireAsync(
                StockLockKey.RawLot(chosen.WarehouseId, chosen.ItemId, order.CustomerId, chosen.RawMessageId),
                cancellationToken))
            {
                _db.InventoryTransactions.Add(new InventoryTransaction(
                    DocumentType.ProductionOrderPause, order.OrderNumber, order.Id,
                    _clock.UtcNow, chosen.WarehouseId, order.CustomerId, chosen.ItemId,
                    rawMessageId: chosen.RawMessageId, productionOrderId: order.Id,
                    quantityKg: releaseKg > 0 ? releaseKg : null,
                    quantityMeter: releaseMeter > 0 ? releaseMeter : null,
                    direction: TransactionDirection.In, createdBy: _currentUser.UserName));
            }
        }
        else
        {
            // Nothing goes back to stock: the pause is purely a state change.
            order.Pause(request.Reason, _currentUser.UserName);
        }

        await _db.SaveChangesAsync(cancellationToken);

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
    string? OverrideReason = null,
    // Set only when the Approval Center dispatches this command after approving a request (spec section 24).
    Guid? ApprovalRequestId = null) : IRequest<ProductionOrderDto>, IApprovalBoundOperation
{
    // Spec section 24: the approval is bound to these exact facts.
    public ApprovalRequestType ApprovalType => ApprovalRequestType.ResumeProduction;

    public Guid ApprovalDocumentId => ProductionOrderId;

    public string OperationSignature =>
        $"ResumeProduction|order={ProductionOrderId}|msg={RawMessageId}|item={ItemId}" +
        $"|kg={ApprovalBinding.Quantity(QuantityKg)}|m={ApprovalBinding.Quantity(QuantityMeter)}";
}

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

        // Loss % against that stage's IMMUTABLE baseline (spec section 15). The baseline
        // is the quantity the stage started from, so the ratio is always "of what went
        // in" - dividing by the output instead would understate the waste (783 baseline
        // with 775 output is 1.02%, not 1.03%).
        var baselineKg = lastCompleted.BaselineKg ?? lastCompleted.InputKg ?? lastCompleted.OutputKg;
        var baselineMeter = lastCompleted.BaselineMeter ?? lastCompleted.InputMeter ?? lastCompleted.OutputMeter;

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

        // Spec section 24: the re-issue is authorised by a stored, SINGLE-USE
        // approval raised by a different user, bound to these exact facts. The
        // old path recorded the caller as both requester and approver, which is
        // exactly the self-approval the separation of duties forbids.
        var decision = await ApprovalRequestGuard.ConsumeAsync(
            _db, _currentUser, request.ApprovalRequestId, request, cancellationToken);

        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, request.ItemId, order.CustomerId, requested, available,
            request.OverrideReason!, decision.RequestedBy, decision.ApprovedBy));

        await Task.CompletedTask;
    }
}