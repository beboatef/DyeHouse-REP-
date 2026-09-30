using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.RawExternalReleases.DTOs;
using DyeHouseERP.Application.RawExternalReleases.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.RawExternalReleases.Commands;

/// <summary>
/// Records material coming BACK from an external processor (spec section 21).
/// The return posts an IN ledger row against the same raw material message and
/// customer the material left under, so the released quantity and the returned
/// quantity always reconcile - and the same physical quantity can never exist
/// twice as available stock (spec section 20).
/// </summary>
public record RecordExternalProcessingReturnCommand(
    Guid ReleaseId, decimal? ReturnedQuantityKg, decimal? ReturnedQuantityMeter,
    DateTime ActualReturnDate, string? Notes = null) : IRequest<RawExternalReleaseDto>;

public class RecordExternalProcessingReturnCommandValidator : AbstractValidator<RecordExternalProcessingReturnCommand>
{
    public RecordExternalProcessingReturnCommandValidator()
    {
        RuleFor(x => x.ReleaseId).NotEmpty();
        RuleFor(x => x).Must(x => x.ReturnedQuantityKg is > 0 || x.ReturnedQuantityMeter is > 0)
            .WithMessage("Specify the returned quantity in KG and/or Meter.");
    }
}

public class RecordExternalProcessingReturnCommandHandler
    : IRequestHandler<RecordExternalProcessingReturnCommand, RawExternalReleaseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;
    private readonly IAllocationLockService _stockLock;

    public RecordExternalProcessingReturnCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDateTime clock, IPeriodCloseService periodClose, IAllocationLockService stockLock)
    { _db = db; _currentUser = currentUser; _clock = clock; _periodClose = periodClose; _stockLock = stockLock; }

    public async Task<RawExternalReleaseDto> Handle(RecordExternalProcessingReturnCommand request, CancellationToken cancellationToken)
    {
        // Spec section 43: recording the return posts an IN row in the ledger.
        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var release = await _db.RawExternalReleases
            .FirstOrDefaultAsync(r => r.Id == request.ReleaseId, cancellationToken)
            ?? throw new NotFoundException("RawExternalRelease", request.ReleaseId);

        var message = await _db.RawMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == release.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", release.RawMessageId);

        // H6: the return posts an IN into the same raw lot the release took out
        // of, under that lot's lock.
        await using (await _stockLock.AcquireAsync(
            StockLockKey.RawLot(message.WarehouseId, release.ItemId, release.CustomerId, release.RawMessageId),
            cancellationToken))
        {

        release.RecordReturn(request.ReturnedQuantityKg, request.ReturnedQuantityMeter,
            request.ActualReturnDate, _currentUser.UserName);

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawIssueExternalRelease, release.ReleaseNumber, release.Id,
            _clock.UtcNow, message.WarehouseId, release.CustomerId, release.ItemId,
            rawMessageId: release.RawMessageId, productionOrderId: release.ProductionOrderId,
            quantityKg: request.ReturnedQuantityKg, quantityMeter: request.ReturnedQuantityMeter,
            direction: TransactionDirection.In, createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);
        }

        return await RawExternalReleaseDtoBuilder.BuildAsync(_db, release, cancellationToken);
    }
}

/// <summary>Correction of external-processor details while the material is still out (spec section 21).</summary>
public record SetExternalProcessingDetailsCommand(
    Guid ReleaseId, string? ExternalParty, string? Stage, decimal? Cost, DateTime? ExpectedReturnDate)
    : IRequest<RawExternalReleaseDto>;

public class SetExternalProcessingDetailsCommandHandler
    : IRequestHandler<SetExternalProcessingDetailsCommand, RawExternalReleaseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public SetExternalProcessingDetailsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    { _db = db; _currentUser = currentUser; }

    public async Task<RawExternalReleaseDto> Handle(SetExternalProcessingDetailsCommand request, CancellationToken cancellationToken)
    {
        var release = await _db.RawExternalReleases
            .FirstOrDefaultAsync(r => r.Id == request.ReleaseId, cancellationToken)
            ?? throw new NotFoundException("RawExternalRelease", request.ReleaseId);

        release.SetExternalProcessingDetails(request.ExternalParty, request.Stage, request.Cost,
            request.ExpectedReturnDate, _currentUser.UserName);

        await _db.SaveChangesAsync(cancellationToken);
        return await RawExternalReleaseDtoBuilder.BuildAsync(_db, release, cancellationToken);
    }
}

/// <summary>
/// Cancels an outstanding external-processing release (spec section 10 - history
/// is never deleted).
///
/// BUSINESS RULE: external processing is a PRODUCTION STAGE, not a separate
/// raw-material release workflow. The quantity left the raw lot when the
/// movement was created (its OUT row belongs to the Production Order / WIP
/// flow), so cancelling is a WORKFLOW-STATE cancellation only: it posts NO
/// ledger rows, never returns stock to the customer's raw-material warehouse,
/// and never restores the customer's raw-material balance. (A genuine return
/// from the processor is posted by RecordExternalProcessingReturnCommand,
/// which is the only path that credits the raw lot back.)
/// </summary>
public record CancelExternalProcessingCommand(Guid ReleaseId, string Reason) : IRequest<RawExternalReleaseDto>;

public class CancelExternalProcessingCommandValidator : AbstractValidator<CancelExternalProcessingCommand>
{
    public CancelExternalProcessingCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class CancelExternalProcessingCommandHandler
    : IRequestHandler<CancelExternalProcessingCommand, RawExternalReleaseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAllocationLockService _stockLock;

    public CancelExternalProcessingCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IAllocationLockService stockLock)
    { _db = db; _currentUser = currentUser; _stockLock = stockLock; }

    public async Task<RawExternalReleaseDto> Handle(CancelExternalProcessingCommand request, CancellationToken cancellationToken)
    {
        var release = await _db.RawExternalReleases
            .FirstOrDefaultAsync(r => r.Id == request.ReleaseId, cancellationToken)
            ?? throw new NotFoundException("RawExternalRelease", request.ReleaseId);

        // The lock key names the raw lot this movement was issued from, so a
        // cancel serializes against a concurrent return on the same lot.
        var message = await _db.RawMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == release.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", release.RawMessageId);

        // R3: the status is re-read INSIDE the lock so a second cancel that was
        // queueing behind the first cannot run twice. NO reversal rows are
        // posted here - see the business rule on the command record above.
        var lockKeys = new[]
        {
            StockLockKey.RawLot(message.WarehouseId, release.ItemId, release.CustomerId, release.RawMessageId)
        };

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {
            var liveStatus = await _db.RawExternalReleases.AsNoTracking()
                .Where(r => r.Id == request.ReleaseId)
                .Select(r => r.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (liveStatus == ExternalProcessingStatus.Cancelled)
                throw new DomainException(
                    $"External processing release '{release.ReleaseNumber}' is already cancelled.");

            // Status change only. Deliberately NO InventoryTransaction rows:
            // the OUT row posted at creation stays in the ledger as the
            // production/WIP consumption, and the customer's raw-material
            // balance must NOT be restored by a cancellation.
            release.CancelExternalProcessing(request.Reason, _currentUser.UserName);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await RawExternalReleaseDtoBuilder.BuildAsync(_db, release, cancellationToken);
    }
}

/// <summary>Shared single-record mapper, so the create/return/detail views agree on every field.</summary>
internal static class RawExternalReleaseDtoBuilder
{
    public static async Task<RawExternalReleaseDto> BuildAsync(
        IApplicationDbContext db, Domain.Entities.RawExternalRelease release, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == release.CustomerId, ct);
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == release.ItemId, ct);
        var message = await db.RawMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == release.RawMessageId, ct);
        var orderNumber = release.ProductionOrderId is null ? null
            : await db.ProductionOrders.AsNoTracking()
                .Where(o => o.Id == release.ProductionOrderId.Value)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(ct);

        return new RawExternalReleaseDto
        {
            Id = release.Id, ReleaseNumber = release.ReleaseNumber, ReleaseDate = release.ReleaseDate,
            CustomerId = release.CustomerId,
            CustomerCode = customer?.Code ?? string.Empty, CustomerName = customer?.Name ?? string.Empty,
            ItemId = release.ItemId,
            ItemCode = item?.Code ?? string.Empty, ItemName = item?.Name ?? string.Empty,
            RawMessageId = release.RawMessageId, MessageNumber = message?.MessageNumber ?? string.Empty,
            QuantityKg = release.QuantityKg, QuantityMeter = release.QuantityMeter,
            Reason = release.Reason, ExternalParty = release.ExternalParty, Notes = release.Notes,
            CreatedBy = release.CreatedBy, CreatedAtUtc = release.CreatedAtUtc,
            ProductionOrderId = release.ProductionOrderId, ProductionOrderNumber = orderNumber,
            ExternalProcessingStage = release.ExternalProcessingStage,
            ExternalProcessingCost = release.ExternalProcessingCost,
            ExpectedReturnDate = release.ExpectedReturnDate,
            ActualReturnDate = release.ActualReturnDate,
            ReturnedQuantityKg = release.ReturnedQuantityKg,
            ReturnedQuantityMeter = release.ReturnedQuantityMeter,
            CancellationReason = release.CancellationReason,
            Status = release.Status
        };
    }
}
