using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawExternalReleases.Commands;

/// <summary>
/// Moves raw material out of the warehouse for a reason OTHER than
/// production consumption (spec section 14): return to customer, external
/// processing, or sale. Always draws from one specific, user-chosen message.
/// </summary>
public record CreateRawExternalReleaseCommand(
    Guid CustomerId, Guid ItemId, Guid RawMessageId,
    decimal? QuantityKg, decimal? QuantityMeter, RawReleaseReason Reason,
    string? ExternalParty, string? Notes,
    bool OverrideNegativeStock = false, string? OverrideReason = null,
    Guid? ProductionOrderId = null, string? ExternalProcessingStage = null,
    decimal? ExternalProcessingCost = null, DateTime? ExpectedReturnDate = null,
    // Set only when the Approval Center dispatches this command after approving a request (spec section 24).
    Guid? ApprovalRequestId = null) : IRequest<RawExternalReleaseDto>, IApprovalBoundOperation
{
    // Spec section 24: the approval is bound to these exact facts - including
    // the release reason, so an approval for an external-processing release
    // cannot authorise a sale.
    public ApprovalRequestType ApprovalType => ApprovalRequestType.RawExternalRelease;

    public Guid ApprovalDocumentId => RawMessageId;

    public string OperationSignature =>
        $"RawExternalRelease|msg={RawMessageId}|item={ItemId}|customer={CustomerId}|reason={Reason}" +
        $"|kg={ApprovalBinding.Quantity(QuantityKg)}|m={ApprovalBinding.Quantity(QuantityMeter)}";
}

public class CreateRawExternalReleaseCommandValidator : AbstractValidator<CreateRawExternalReleaseCommand>
{
    public CreateRawExternalReleaseCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the quantity to release in KG and/or Meter.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.OverrideNegativeStock);
    }
}

public class CreateRawExternalReleaseCommandHandler : IRequestHandler<CreateRawExternalReleaseCommand, RawExternalReleaseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public CreateRawExternalReleaseCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IInventoryLedgerService ledger, IDateTime clock, IPeriodCloseService periodClose,
        IAllocationLockService stockLock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _ledger = ledger;
        _clock = clock;
        _periodClose = periodClose;
        _stockLock = stockLock;
    }

    public async Task<RawExternalReleaseDto> Handle(CreateRawExternalReleaseCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: releasing raw material out of the warehouse is a stock posting.
        await using var periodGuard = await _periodClose.BeginPostingAsync(_clock.UtcNow, cancellationToken);

        var message = await _db.RawMessages.FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for release (status: {message.Status}).");

        // A release posts a NEW OUT row against the message's own warehouse, so a
        // deactivated warehouse cannot take part: its history stays readable and
        // untouched, but no new stock row may be written into it.
        await WarehouseGuard.EnsureUsableForNewMovementAsync(
            _db, message.WarehouseId, cancellationToken, "to release raw material out of it");

        // H6: releasing material to an external processor is an OUT from the same
        // raw lot the allocation path locks, so the two cannot oversubscribe it
        // concurrently.
        await using (await _stockLock.AcquireAsync(
            StockLockKey.RawLot(message.WarehouseId, request.ItemId, request.CustomerId, message.Id), cancellationToken))
        {

        var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(message.Id, request.ItemId, request.CustomerId, message.WarehouseId, cancellationToken);

        var kgShort = request.QuantityKg.HasValue && request.QuantityKg.Value > balanceKg;
        var meterShort = request.QuantityMeter.HasValue && request.QuantityMeter.Value > balanceMeter;

        if (kgShort || meterShort)
        {
            var requested = kgShort ? request.QuantityKg!.Value : request.QuantityMeter!.Value;
            var available = kgShort ? balanceKg : balanceMeter;

            if (!request.OverrideNegativeStock)
                throw new NegativeStockException(available, requested);

            if (!_currentUser.HasPermission(Permissions.InventoryAllowNegativeStock) || !_currentUser.HasPermission(Permissions.InventoryApproveNegativeStock))
                throw new UnauthorizedAccessException(
                    $"Overriding negative stock requires both '{Permissions.InventoryAllowNegativeStock}' and '{Permissions.InventoryApproveNegativeStock}' permissions.");

            // Spec section 24: the override is authorised by a stored,
            // SINGLE-USE approval request raised by a DIFFERENT user, matched
            // against this movement's exact facts. The old path recorded the
            // caller as both requester and approver - the self-approval the
            // separation of duties forbids - and posted with no approval at all.
            var decision = await ApprovalRequestGuard.ConsumeAsync(
                _db, _currentUser, request.ApprovalRequestId, request, cancellationToken);

            _db.NegativeStockOverrides.Add(new NegativeStockOverride(
                message.Id, request.ItemId, request.CustomerId, requested, available,
                request.OverrideReason!, decision.RequestedBy, decision.ApprovedBy));
        }

        var releaseNumber = await _numberGenerator.NextAsync(DocumentType.RawIssueExternalRelease, cancellationToken: cancellationToken);

        var release = new RawExternalRelease(
            releaseNumber, _clock.UtcNow, request.CustomerId, request.ItemId, message.Id,
            request.QuantityKg, request.QuantityMeter, request.Reason, _currentUser.UserName,
            request.ExternalParty, request.Notes,
            productionOrderId: request.ProductionOrderId,
            externalProcessingStage: request.ExternalProcessingStage,
            externalProcessingCost: request.ExternalProcessingCost,
            expectedReturnDate: request.ExpectedReturnDate);

        _db.RawExternalReleases.Add(release);

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawIssueExternalRelease, release.ReleaseNumber, release.Id,
            _clock.UtcNow, message.WarehouseId, request.CustomerId, request.ItemId,
            rawMessageId: message.Id, productionOrderId: null,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: TransactionDirection.Out, createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken);

        return new RawExternalReleaseDto
        {
            Id = release.Id,
            ReleaseNumber = release.ReleaseNumber,
            ReleaseDate = release.ReleaseDate,
            CustomerId = release.CustomerId,
            CustomerCode = customer?.Code ?? string.Empty,
            CustomerName = customer?.Name ?? string.Empty,
            ItemId = release.ItemId,
            ItemCode = item?.Code ?? string.Empty,
            ItemName = item?.Name ?? string.Empty,
            RawMessageId = release.RawMessageId,
            MessageNumber = message.MessageNumber,
            QuantityKg = release.QuantityKg,
            QuantityMeter = release.QuantityMeter,
            Reason = release.Reason,
            ExternalParty = release.ExternalParty,
            Notes = release.Notes,
            CreatedBy = release.CreatedBy,
            CreatedAtUtc = release.CreatedAtUtc,
            ProductionOrderId = release.ProductionOrderId,
            ExternalProcessingStage = release.ExternalProcessingStage,
            ExternalProcessingCost = release.ExternalProcessingCost,
            ExpectedReturnDate = release.ExpectedReturnDate,
            Status = release.Status
        };
        }
    }
}
