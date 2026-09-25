using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
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
    bool OverrideNegativeStock = false, string? OverrideReason = null) : IRequest<RawExternalReleaseDto>;

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

    public CreateRawExternalReleaseCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IInventoryLedgerService ledger, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<RawExternalReleaseDto> Handle(CreateRawExternalReleaseCommand request, CancellationToken cancellationToken)
    {
        var message = await _db.RawMessages.FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        if (!message.IsAvailableForAllocation)
            throw new DomainException(
                $"Message '{message.MessageNumber}' is not available for release (inspection status: {message.InspectionStatus}, status: {message.Status}).");

        var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(message.Id, request.ItemId, request.CustomerId, message.WarehouseId, cancellationToken);

        var kgShort = request.QuantityKg.HasValue && request.QuantityKg.Value > balanceKg;
        var meterShort = request.QuantityMeter.HasValue && request.QuantityMeter.Value > balanceMeter;

        if (kgShort || meterShort)
        {
            var requested = kgShort ? request.QuantityKg!.Value : request.QuantityMeter!.Value;
            var available = kgShort ? balanceKg : balanceMeter;

            if (!request.OverrideNegativeStock)
                throw new NegativeStockException(available, requested);

            if (!_currentUser.IsInRole(Permissions.InventoryAllowNegativeStock))
                throw new UnauthorizedAccessException(
                    $"Overriding negative stock requires the '{Permissions.InventoryAllowNegativeStock}' permission.");

            _db.NegativeStockOverrides.Add(new NegativeStockOverride(
                message.Id, request.ItemId, request.CustomerId, requested, available,
                request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));
        }

        var releaseNumber = await _numberGenerator.NextAsync(DocumentType.RawIssueExternalRelease, cancellationToken: cancellationToken);

        var release = new RawExternalRelease(
            releaseNumber, _clock.UtcNow, request.CustomerId, request.ItemId, message.Id,
            request.QuantityKg, request.QuantityMeter, request.Reason, _currentUser.UserName,
            request.ExternalParty, request.Notes);

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
            CreatedAtUtc = release.CreatedAtUtc
        };
    }
}
