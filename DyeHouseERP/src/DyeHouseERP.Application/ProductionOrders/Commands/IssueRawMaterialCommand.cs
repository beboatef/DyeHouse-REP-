using System.Text.Json;
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
/// THE canonical workflow (spec section 5): issuing raw material from a customer's
/// lot to the floor CREATES the Job Order. The old order-first-then-allocate path is
/// no longer reachable from the API - an ordinary order cannot exist without its issue.
///
/// Both entry points use this one command:
///   * <b>Workflow 1</b> - from a Formation Request: pass <see cref="FormationRequestId"/>
///     and (optionally) <see cref="FormationBasinId"/>. The request is planning-only: it is
///     NEVER turned into stock, and it is never required for an issue.
///   * <b>Workflow 2</b> - a direct issue with no Formation Request: leave both null
///     (no fake request is created).
///
/// The ACTUAL issued quantities (<see cref="ActualQuantityKg"/>/<see cref="ActualQuantityMeter"/>)
/// become the order's opening quantities (RequestedQuantityKg/Meter) and the formation
/// stage's immutable baseline. Planned quantities are never copied into the order and are
/// never enforced - the actual may differ from the planned by any amount, and a partial
/// issue is recorded exactly as issued (it simply means the 100%-allocation rule is not met
/// yet, so production cannot start).
///
/// Everything happens inside ONE database transaction: the OUT ledger row, the Job Order
/// and its allocation row commit together, or nothing does.
/// </summary>
public record IssueRawMaterialCommand(
    Guid CustomerId,
    Guid ItemId,
    Guid RawMessageId,
    DateTime OrderDate,
    decimal? ActualQuantityKg,
    decimal? ActualQuantityMeter,
    string? Color = null,
    string? RawOrigin = null,
    string? CustomerReference = null,
    string? Notes = null,
    ProductionPriority Priority = ProductionPriority.Normal,
    JobOrderType JobOrderType = JobOrderType.ClosedLine,
    Guid? FormationRequestId = null,
    Guid? FormationBasinId = null,
    // Client idempotency token: a retried/double-submitted confirmation with the same
    // token returns the existing order instead of posting a second issue.
    Guid? RequestId = null,
    bool OverrideNegativeStock = false,
    string? OverrideReason = null,
    // Set only when the Approval Center dispatches this command after approving a request (spec section 24).
    Guid? ApprovalRequestId = null,
    // Set when this issue raises the Job Order from a CUSTOMER RETURN of processed goods (spec sections
    // 32-33). The return is a reference only - the Job Order, the OUT ledger row and its allocation are
    // created exactly as they are for any other issue, so a return-based order is a real production
    // order and not a special document type.
    Guid? CustomerReturnId = null,
    // Who carries the cost of the order this issue creates (spec section 20). MANDATORY whenever
    // CustomerReturnId is set: rework of a returned batch can legitimately be a quality claim the
    // factory absorbs or a charge the customer carries, and the two must never be guessed.
    CostBearer? CostBearer = null,
    // Optional starting stage for the order this issue creates (spec section 33): a job raised from
    // a customer RETURN may begin where the rework begins instead of at التشكيل. It must name an
    // existing, ACTIVE stage; empty keeps the ordinary formation start. The issue itself - and so
    // the single raw deduction - is completely unaffected: one OUT row either way.
    Guid? StartingStageId = null) : IRequest<ProductionOrderDto>, IApprovalBoundOperation
{
    public ApprovalRequestType ApprovalType => ApprovalRequestType.RawIssue;

    /// <summary>The issue has no order yet, so the approval is bound to the customer's lot.</summary>
    public Guid ApprovalDocumentId => RawMessageId;

    public string OperationSignature =>
        $"RawIssue|customer={CustomerId}|item={ItemId}|msg={RawMessageId}" +
        $"|kg={ApprovalBinding.Quantity(ActualQuantityKg)}|m={ApprovalBinding.Quantity(ActualQuantityMeter)}" +
        $"|fr={FormationRequestId?.ToString() ?? "-"}|basin={FormationBasinId?.ToString() ?? "-"}" +
        // The cost-responsibility decision is part of what an approval authorizes, so an approval
        // raised for one account can never be spent on the other (spec section 24).
        $"|return={CustomerReturnId?.ToString() ?? "-"}|bearer={(CostBearer.HasValue ? ((int)CostBearer.Value).ToString() : "-")}";

    /// <summary>
    /// Normalizes the command for a separation-of-duties request: always an override,
    /// always with the request's own reason, and never carrying an approver.
    /// </summary>
    public IssueRawMaterialCommand AsApprovalOverride(string reason, Guid? approvalRequestId) => this with
    {
        OverrideNegativeStock = true,
        OverrideReason = reason,
        ApprovalRequestId = approvalRequestId
    };
}

public class IssueRawMaterialCommandValidator : AbstractValidator<IssueRawMaterialCommand>
{
    public IssueRawMaterialCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty().WithMessage("Choose the item being issued - a raw lot may carry several items.");
        RuleFor(x => x.RawMessageId).NotEmpty().WithMessage("Choose the raw material lot the material is issued from.");
        RuleFor(x => x.JobOrderType).IsInEnum();

        // Spec sections 32-33: an order raised from a customer return must state who carries its cost.
        // Deliberately a validation failure rather than a default - silently billing a courtesy rework
        // to the customer (or absorbing work that should be charged) is exactly the mistake this rule
        // exists to prevent.
        RuleFor(x => x.CostBearer)
            .NotNull()
            .When(x => x.CustomerReturnId.HasValue)
            .WithMessage("Choose who carries the cost of an order raised from a customer return: on the customer's account (على حساب العميل) or on the company's account (على حساب الشركة).");

        RuleFor(x => x.CostBearer)
            .Null()
            .When(x => !x.CustomerReturnId.HasValue)
            .WithMessage("The cost responsibility of an ordinary issue is the customer's; it can only be stated for an order raised from a customer return.");
        RuleFor(x => x)
            .Must(x => x.ActualQuantityKg is > 0 || x.ActualQuantityMeter is > 0)
            .WithMessage("Enter the ACTUAL issued quantity in KG and/or Meter - it becomes the Job Order's opening quantity and is never derived automatically.");
        RuleFor(x => x.OverrideReason)
            .NotEmpty()
            .When(x => x.OverrideNegativeStock)
            .WithMessage("A reason is required when overriding the negative-stock check.");
    }
}

public class IssueRawMaterialCommandHandler : IRequestHandler<IssueRawMaterialCommand, ProductionOrderDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public IssueRawMaterialCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IInventoryLedgerService ledger,
        IDocumentNumberGenerator numberGenerator, IDateTime clock, IPeriodCloseService periodClose,
        IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _ledger = ledger; _numberGenerator = numberGenerator;
        _clock = clock; _periodClose = periodClose; _stockLock = stockLock;
    }

    public async Task<ProductionOrderDto> Handle(IssueRawMaterialCommand request, CancellationToken cancellationToken)
    {
        // ---- Idempotency (spec section 5) ------------------------------------
        // A confirmation retried with the same token returns the SAME order and posts
        // nothing. The unique index on IssueRequestId is the database backstop for a
        // true race between two identical requests.
        if (request.RequestId.HasValue)
        {
            var existingId = await _db.ProductionOrders.AsNoTracking()
                .Where(o => o.IssueRequestId == request.RequestId.Value)
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId.HasValue)
                return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, existingId.Value, cancellationToken);
        }

        // A raw issue is a stock posting, so its date must fall outside any closed period.
        await using var periodGuard = await _periodClose.BeginPostingAsync(request.OrderDate, cancellationToken);

        // Spec sections 5-6: the issued quantity may only use the item's configured units,
        // and BOTH are required when the item demands both measurements.
        await ItemUnitGuard.EnsureValidQuantitiesAsync(
            _db, request.ItemId, request.ActualQuantityKg, request.ActualQuantityMeter,
            cancellationToken, "on this raw issue");

        var message = await _db.RawMessages
            .Include(m => m.Lines)
            .FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for issuing (status: {message.Status}). Material cannot be issued from it.");

        // The caller must name an item that genuinely exists on this lot - a multi-line
        // message would otherwise make "first matching line" a silent guess.
        if (!message.Lines.Any(l => l.ItemId == request.ItemId))
            throw new DomainException(
                $"Item '{request.ItemId}' is not a line on message '{message.MessageNumber}'. Pick one of the message's item lines.");

        // The issue posts an OUT row against the lot's warehouse, so that warehouse must
        // exist and still be active.
        var rawWarehouse = await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, message.WarehouseId, cancellationToken, "to issue raw material out of it");
        if (rawWarehouse.Kind != WarehouseKind.RawMaterial)
            throw new DomainException("Customer-owned raw fabric can only be issued from a RawMaterial warehouse.");

        // The customer's lot belongs to its own customer - issuing another customer's
        // material would post stock against the wrong account.
        if (message.CustomerId != request.CustomerId)
            throw new DomainException(
                $"Message '{message.MessageNumber}' belongs to a different customer than this issue.");

        // ---- Optional planning references (Workflow 1) -----------------------
        await ValidateFormationReferenceAsync(request, cancellationToken);

        // ---- Optional customer-return reference (spec sections 32-33) --------
        await ValidateCustomerReturnAsync(request, cancellationToken);

        // The stage route is snapshotted at issue time; a missing formation stage is a
        // configuration error and must roll the whole issue back rather than create an
        // order that can never be worked.
        var activeStages = await _db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken);

        // A requested starting stage must be one of the ACTIVE stages; the domain lookup below
        // refuses an unknown or deactivated id, so a stale screen selection cannot become a route.
        if (request.StartingStageId.HasValue)
            _ = activeStages.FirstOrDefault(s => s.Id == request.StartingStageId.Value)
                ?? throw new DomainException(
                    "The selected starting stage is not one of the active production stages. Choose an existing, " +
                    "active stage (or leave it empty to start at the formation stage).");

        if (!activeStages.Any(s => s.IsFormationStage))
            throw new DomainException(
                "No active production stage is marked as the formation stage (IsFormationStage). Configure one before issuing raw material.");

        var orderNumber = await _numberGenerator.NextAsync(DocumentType.ProductionOrder, cancellationToken: cancellationToken);

        // ---- The single atomic unit of work ---------------------------------
        // Lock the raw lot exactly as every other poster of this dimension does, then
        // read the balance and write the movement, the order and its allocation inside
        // that lock and in ONE SaveChangesAsync (one database transaction).
        await using (await _stockLock.AcquireAsync(
            StockLockKey.RawLot(message.WarehouseId, request.ItemId, request.CustomerId, message.Id), cancellationToken))
        {
            var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(
                message.Id, request.ItemId, request.CustomerId, message.WarehouseId, cancellationToken);

            await EnsureSufficientBalanceAsync(request, message, orderId: null, balanceKg, balanceMeter, cancellationToken);

            // The order's OPENING quantities are the ACTUAL issued ones - never the planned
            // ones, and never rounded or inferred. The formation stage's baseline is the same
            // figure (BuildStageRoute snapshots RequestedQuantityKg/Meter).
            var order = new ProductionOrder(
                orderNumber, request.CustomerId, request.ItemId, request.OrderDate, _currentUser.UserName,
                color: request.Color,
                requestedQuantityKg: request.ActualQuantityKg,
                requestedQuantityMeter: request.ActualQuantityMeter,
                rawOrigin: request.RawOrigin,
                customerReference: request.CustomerReference,
                notes: request.Notes,
                priority: request.Priority,
                jobOrderType: request.JobOrderType);

            order.BuildStageRoute(activeStages, _currentUser.UserName, request.StartingStageId);

            // The issue IS the raw allocation: creating the matching allocation row here
            // means the existing 100%-allocation rule is satisfied by the act of issuing,
            // and no separate allocate step can post a SECOND OUT for the same material.
            order.AllocateRaw(message.Id, request.ItemId, request.ActualQuantityKg, request.ActualQuantityMeter, _currentUser.UserName);

            var issueRow = new InventoryTransaction(
                DocumentType.ProductionOrder, order.OrderNumber, order.Id,
                request.OrderDate, message.WarehouseId, request.CustomerId, request.ItemId,
                rawMessageId: message.Id, productionOrderId: order.Id,
                quantityKg: request.ActualQuantityKg, quantityMeter: request.ActualQuantityMeter,
                direction: TransactionDirection.Out, createdBy: _currentUser.UserName);

            // Link the one generated order to the one issue transaction (+ optional planning
            // references and the optional customer return), so the relationship is data, not a
            // convention. The cost bearer is recorded on the ORDER for the same reason: cost
            // responsibility is a property of the job, not of the screen that raised it.
            order.LinkIssue(
                issueRow.Id, request.RequestId, request.FormationRequestId, request.FormationBasinId,
                request.CustomerReturnId, request.CostBearer);

            _db.ProductionOrders.Add(order);
            _db.InventoryTransactions.Add(issueRow);

            _db.AuditLogEntries.Add(new AuditLogEntry(
                _clock.UtcNow, _currentUser.UserName, "IssueRawMaterial", "ProductionOrder", order.Id.ToString(),
                beforeDataJson: null,
                afterDataJson: JsonSerializer.Serialize(new
                {
                    orderNumber = order.OrderNumber,
                    rawMessageId = message.Id,
                    messageNumber = message.MessageNumber,
                    itemId = request.ItemId,
                    warehouseId = message.WarehouseId,
                    actualQuantityKg = request.ActualQuantityKg,
                    actualQuantityMeter = request.ActualQuantityMeter,
                    formationRequestId = request.FormationRequestId,
                    formationBasinId = request.FormationBasinId,
                    customerReturnId = request.CustomerReturnId,
                    costBearer = request.CostBearer?.ToString(),
                    sourceIssueTransactionId = issueRow.Id
                }),
                ipAddress: null,
                reason: request.Notes));

            // ONE transaction: the OUT row, the order, its allocation and the audit entry
            // all commit together or roll back together.
            await _db.SaveChangesAsync(cancellationToken);

            return await GetProductionOrderByIdQueryHandler.LoadDtoAsync(_db, order.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Validates the optional customer-return reference (spec sections 32-33). The return must exist,
    /// must not be cancelled, and must belong to the customer whose material is being issued: a return
    /// is the justification for the rework, so pointing an issue at another customer's return would
    /// make the traceability chain lie. Nothing here is required for an ordinary issue.
    /// </summary>
    private async Task ValidateCustomerReturnAsync(IssueRawMaterialCommand request, CancellationToken cancellationToken)
    {
        if (!request.CustomerReturnId.HasValue) return;

        var customerReturn = await _db.CustomerReturns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.CustomerReturnId.Value, cancellationToken)
            ?? throw new NotFoundException("CustomerReturn", request.CustomerReturnId.Value);

        if (customerReturn.IsCancelled)
            throw new DomainException(
                $"Customer return {customerReturn.ReturnNumber} is cancelled, so no Job Order can be raised from it.");

        if (customerReturn.CustomerId != request.CustomerId)
            throw new DomainException(
                $"Customer return {customerReturn.ReturnNumber} belongs to a different customer than this issue.");
    }

    private async Task ValidateFormationReferenceAsync(IssueRawMaterialCommand request, CancellationToken cancellationToken)
    {
        if (request.FormationBasinId.HasValue && !request.FormationRequestId.HasValue)
            throw new DomainException(
                "A formation basin can only be linked together with its formation request. Supply the request, or issue directly with neither.");

        if (!request.FormationRequestId.HasValue)
            return;

        var formationRequest = await _db.FormationRequests.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.FormationRequestId.Value, cancellationToken)
            ?? throw new NotFoundException("FormationRequest", request.FormationRequestId.Value);

        if (formationRequest.CustomerId != request.CustomerId)
            throw new DomainException(
                $"Formation request '{formationRequest.RequestNumber}' belongs to a different customer than this issue.");

        if (!request.FormationBasinId.HasValue)
            return;

        var basinGroupIds = await _db.FormationGroups.AsNoTracking()
            .Where(g => g.FormationRequestId == formationRequest.Id)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        var basinExists = await _db.FormationBasins.AsNoTracking()
            .AnyAsync(b => b.Id == request.FormationBasinId.Value && basinGroupIds.Contains(b.FormationGroupId), cancellationToken);

        if (! basinExists)
            throw new DomainException(
                $"Basin '{request.FormationBasinId}' does not belong to formation request '{formationRequest.RequestNumber}'.");
    }

    /// <summary>
    /// The shared negative-stock path: refuse when the lot is short unless the caller both
    /// holds the override permissions and consumes a stored, single-use approval raised by a
    /// DIFFERENT user (requester ≠ approver). Nothing here can be bypassed from the API.
    /// </summary>
    private async Task EnsureSufficientBalanceAsync(
        IssueRawMaterialCommand request, RawMessage message, Guid? orderId,
        decimal balanceKg, decimal balanceMeter, CancellationToken cancellationToken)
    {
        var kgShort = request.ActualQuantityKg.HasValue && request.ActualQuantityKg.Value > balanceKg;
        var meterShort = request.ActualQuantityMeter.HasValue && request.ActualQuantityMeter.Value > balanceMeter;

        if (!kgShort && !meterShort)
            return;

        var requested = kgShort ? request.ActualQuantityKg!.Value : request.ActualQuantityMeter!.Value;
        var available = kgShort ? balanceKg : balanceMeter;

        if (!request.OverrideNegativeStock)
            throw new NegativeStockException(available, requested);

        if (!_currentUser.HasPermission(Permissions.InventoryAllowNegativeStock)
            || !_currentUser.HasPermission(Permissions.InventoryApproveNegativeStock))
            throw new UnauthorizedAccessException(
                $"Overriding negative stock requires both '{Permissions.InventoryAllowNegativeStock}' and '{Permissions.InventoryApproveNegativeStock}' permissions.");

        var decision = await ApprovalRequestGuard.ConsumeAsync(
            _db, _currentUser, request.ApprovalRequestId, request, cancellationToken);

        _db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, request.ItemId, request.CustomerId, requested, available,
            request.OverrideReason!, decision.RequestedBy, decision.ApprovedBy));

        await Task.CompletedTask;
    }
}
