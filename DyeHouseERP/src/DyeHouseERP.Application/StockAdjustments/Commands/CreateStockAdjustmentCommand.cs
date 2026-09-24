using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.StockAdjustments.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.StockAdjustments.Commands;

/// <summary>
/// Manually corrects a customer's balance on a specific message/item (spec
/// section 16). A Decrease that would take the balance negative requires the
/// same override + permission + audit trail as every other outbound
/// movement (spec section 17).
/// </summary>
public record CreateStockAdjustmentCommand(
    Guid CustomerId, Guid ItemId, Guid RawMessageId, AdjustmentType Type,
    decimal? QuantityKg, decimal? QuantityMeter, string Reason, string? Notes, string? ApprovedBy,
    bool OverrideNegativeStock = false, string? OverrideReason = null) : IRequest<StockAdjustmentDto>;

public class CreateStockAdjustmentCommandValidator : AbstractValidator<CreateStockAdjustmentCommand>
{
    public CreateStockAdjustmentCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
        RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the adjustment quantity in KG and/or Meter.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.OverrideNegativeStock);
    }
}

public class CreateStockAdjustmentCommandHandler : IRequestHandler<CreateStockAdjustmentCommand, StockAdjustmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDateTime _clock;
    private readonly IPeriodCloseService _periodClose;

    public CreateStockAdjustmentCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IInventoryLedgerService ledger, IDateTime clock, IPeriodCloseService periodClose)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator; _ledger = ledger; _clock = clock; _periodClose = periodClose;
    }

    public async Task<StockAdjustmentDto> Handle(CreateStockAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var message = await _db.RawMessages.FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        await _periodClose.EnsureOpenAsync(_clock.UtcNow, cancellationToken);

        var (beforeKg, beforeMeter) = await _ledger.GetCustomerBalanceAsync(message.Id, request.ItemId, request.CustomerId, cancellationToken);

        if (request.Type == AdjustmentType.Decrease)
        {
            var kgShort = request.QuantityKg.HasValue && request.QuantityKg.Value > beforeKg;
            var meterShort = request.QuantityMeter.HasValue && request.QuantityMeter.Value > beforeMeter;

            if (kgShort || meterShort)
            {
                var requested = kgShort ? request.QuantityKg!.Value : request.QuantityMeter!.Value;
                var available = kgShort ? beforeKg : beforeMeter;

                if (!request.OverrideNegativeStock)
                    throw new Domain.Exceptions.NegativeStockException(available, requested);
                if (!_currentUser.IsInRole(Permissions.InventoryAllowNegativeStock))
                    throw new UnauthorizedAccessException($"Overriding negative stock requires the '{Permissions.InventoryAllowNegativeStock}' permission.");

                _db.NegativeStockOverrides.Add(new NegativeStockOverride(
                    message.Id, request.ItemId, request.CustomerId, requested, available,
                    request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));
            }
        }

        var adjustmentNumber = await _numberGenerator.NextAsync(DocumentType.StockAdjustment, cancellationToken: cancellationToken);

        var adjustment = new StockAdjustment(
            adjustmentNumber, _clock.UtcNow, request.CustomerId, request.ItemId, message.Id, message.WarehouseId,
            request.Type, beforeKg, beforeMeter, request.QuantityKg, request.QuantityMeter,
            request.Reason, _currentUser.UserName, request.Notes, request.ApprovedBy);

        _db.StockAdjustments.Add(adjustment);

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.StockAdjustment, adjustment.AdjustmentNumber, adjustment.Id,
            _clock.UtcNow, message.WarehouseId, request.CustomerId, request.ItemId,
            rawMessageId: message.Id, productionOrderId: null,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: request.Type == AdjustmentType.Increase ? TransactionDirection.In : TransactionDirection.Out,
            createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken);

        return new StockAdjustmentDto
        {
            Id = adjustment.Id, AdjustmentNumber = adjustment.AdjustmentNumber, AdjustmentDate = adjustment.AdjustmentDate,
            CustomerId = adjustment.CustomerId, CustomerCode = customer?.Code ?? "", CustomerName = customer?.Name ?? "",
            ItemId = adjustment.ItemId, ItemCode = item?.Code ?? "", ItemName = item?.Name ?? "",
            RawMessageId = adjustment.RawMessageId, MessageNumber = message.MessageNumber, Type = adjustment.Type,
            QuantityBeforeKg = adjustment.QuantityBeforeKg, QuantityBeforeMeter = adjustment.QuantityBeforeMeter,
            AdjustmentQuantityKg = adjustment.AdjustmentQuantityKg, AdjustmentQuantityMeter = adjustment.AdjustmentQuantityMeter,
            QuantityAfterKg = adjustment.QuantityAfterKg, QuantityAfterMeter = adjustment.QuantityAfterMeter,
            Reason = adjustment.Reason, Notes = adjustment.Notes, ApprovedBy = adjustment.ApprovedBy,
            CreatedBy = adjustment.CreatedBy, CreatedAtUtc = adjustment.CreatedAtUtc
        };
    }
}
