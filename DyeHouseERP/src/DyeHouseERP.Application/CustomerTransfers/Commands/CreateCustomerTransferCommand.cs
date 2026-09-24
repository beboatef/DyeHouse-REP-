using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerTransfers.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.CustomerTransfers.Commands;

/// <summary>
/// Moves ownership of part of a raw message's quantity from one customer to
/// another (spec section 15). The message itself is never edited - this
/// posts an OUT ledger row under the sending customer and an IN row under
/// the receiving customer, on the same RawMessageId/ItemId, so both
/// customers' own history stays fully traceable.
/// </summary>
public record CreateCustomerTransferCommand(
    Guid FromCustomerId, Guid ToCustomerId, Guid RawMessageId, Guid ItemId,
    decimal? QuantityKg, decimal? QuantityMeter, string Reason, string? Notes,
    bool OverrideNegativeStock = false, string? OverrideReason = null) : IRequest<CustomerTransferDto>;

public class CreateCustomerTransferCommandValidator : AbstractValidator<CreateCustomerTransferCommand>
{
    public CreateCustomerTransferCommandValidator()
    {
        RuleFor(x => x.FromCustomerId).NotEmpty();
        RuleFor(x => x.ToCustomerId).NotEmpty().NotEqual(x => x.FromCustomerId)
            .WithMessage("The receiving customer must be different from the sending customer.");
        RuleFor(x => x.RawMessageId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
        RuleFor(x => x).Must(x => x.QuantityKg is > 0 || x.QuantityMeter is > 0)
            .WithMessage("Specify the quantity to transfer in KG and/or Meter.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.OverrideNegativeStock);
    }
}

public class CreateCustomerTransferCommandHandler : IRequestHandler<CreateCustomerTransferCommand, CustomerTransferDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IInventoryLedgerService _ledger;
    private readonly IDateTime _clock;

    public CreateCustomerTransferCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDocumentNumberGenerator numberGenerator,
        IInventoryLedgerService ledger, IDateTime clock)
    {
        _db = db;
        _currentUser = currentUser;
        _numberGenerator = numberGenerator;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<CustomerTransferDto> Handle(CreateCustomerTransferCommand request, CancellationToken cancellationToken)
    {
        var message = await _db.RawMessages.FirstOrDefaultAsync(m => m.Id == request.RawMessageId, cancellationToken)
            ?? throw new NotFoundException("RawMessage", request.RawMessageId);

        var (balanceKg, balanceMeter) = await _ledger.GetCustomerBalanceAsync(message.Id, request.ItemId, request.FromCustomerId, cancellationToken);

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
                message.Id, request.ItemId, request.FromCustomerId, requested, available,
                request.OverrideReason!, _currentUser.UserName, _currentUser.UserName));
        }

        var transferNumber = await _numberGenerator.NextAsync(DocumentType.CustomerTransfer, cancellationToken: cancellationToken);

        var transfer = new CustomerTransfer(
            transferNumber, _clock.UtcNow, request.FromCustomerId, request.ToCustomerId,
            message.Id, request.ItemId, request.QuantityKg, request.QuantityMeter,
            request.Reason, _currentUser.UserName, request.Notes);

        _db.CustomerTransfers.Add(transfer);

        // OUT under the sending customer, IN under the receiving customer -
        // same message, same item. Net effect on total message stock is
        // zero; the ownership dimension is what moves (spec section 15).
        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.CustomerTransfer, transfer.TransferNumber, transfer.Id,
            _clock.UtcNow, message.WarehouseId, request.FromCustomerId, request.ItemId,
            rawMessageId: message.Id, productionOrderId: null,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: TransactionDirection.Out, createdBy: _currentUser.UserName));

        _db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.CustomerTransfer, transfer.TransferNumber, transfer.Id,
            _clock.UtcNow, message.WarehouseId, request.ToCustomerId, request.ItemId,
            rawMessageId: message.Id, productionOrderId: null,
            quantityKg: request.QuantityKg, quantityMeter: request.QuantityMeter,
            direction: TransactionDirection.In, createdBy: _currentUser.UserName));

        await _db.SaveChangesAsync(cancellationToken);

        var fromCustomer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.FromCustomerId, cancellationToken);
        var toCustomer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ToCustomerId, cancellationToken);
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken);

        return new CustomerTransferDto
        {
            Id = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromCustomerId = transfer.FromCustomerId,
            FromCustomerCode = fromCustomer?.Code ?? string.Empty,
            FromCustomerName = fromCustomer?.Name ?? string.Empty,
            ToCustomerId = transfer.ToCustomerId,
            ToCustomerCode = toCustomer?.Code ?? string.Empty,
            ToCustomerName = toCustomer?.Name ?? string.Empty,
            RawMessageId = transfer.RawMessageId,
            MessageNumber = message.MessageNumber,
            ItemId = transfer.ItemId,
            ItemCode = item?.Code ?? string.Empty,
            ItemName = item?.Name ?? string.Empty,
            QuantityKg = transfer.QuantityKg,
            QuantityMeter = transfer.QuantityMeter,
            Reason = transfer.Reason,
            Notes = transfer.Notes,
            CreatedBy = transfer.CreatedBy,
            CreatedAtUtc = transfer.CreatedAtUtc
        };
    }
}
