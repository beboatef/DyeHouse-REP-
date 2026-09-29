using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Purchases.DTOs;
using DyeHouseERP.Application.Purchases.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Purchases.Commands;

public class PurchaseReceiptLineInput
{
    public Guid MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public MaterialUnit Unit { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>When receiving against an approved order, the order line this row fulfils.</summary>
    public Guid? PurchaseOrderLineId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Receives materials / chemicals / spare parts / operating supplies into a
/// warehouse (spec section 35). This is the document with a real stock effect:
/// one IN MaterialTransaction per line, written to the append-only material
/// ledger, plus the purchase order's received quantities when one is named.
/// Nothing here touches raw material - that is always customer-owned.
/// </summary>
public record CreatePurchaseReceiptCommand(
    DateTime ReceiptDate,
    Guid SupplierId,
    Guid WarehouseId,
    Guid? PurchaseOrderId = null,
    string? SupplierDocumentNumber = null,
    string? Notes = null,
    List<PurchaseReceiptLineInput>? Lines = null) : IRequest<PurchaseReceiptDto>;

public class CreatePurchaseReceiptCommandValidator : AbstractValidator<CreatePurchaseReceiptCommand>
{
    public CreatePurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A goods receipt must contain at least one line.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

public class CreatePurchaseReceiptCommandHandler : IRequestHandler<CreatePurchaseReceiptCommand, PurchaseReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDocumentNumberGenerator _numberGenerator;
    private readonly IPeriodCloseService _periodClose;
    private readonly ISender _mediator;
    private readonly IAllocationLockService _stockLock;

    public CreatePurchaseReceiptCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser,
        IDocumentNumberGenerator numberGenerator, IPeriodCloseService periodClose, ISender mediator,
        IAllocationLockService stockLock)
    {
        _db = db; _currentUser = currentUser; _numberGenerator = numberGenerator;
        _periodClose = periodClose; _mediator = mediator; _stockLock = stockLock;
    }

    public async Task<PurchaseReceiptDto> Handle(CreatePurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
        await _periodClose.EnsureOpenAsync(request.ReceiptDate, cancellationToken);
        await PurchaseRules.EnsureSupplierExists(_db, request.SupplierId, cancellationToken);
        await PurchaseRules.EnsureWarehouseExists(_db, request.WarehouseId, cancellationToken);

        PurchaseOrder? order = null;
        if (request.PurchaseOrderId.HasValue)
        {
            order = await _db.PurchaseOrders.Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId.Value, cancellationToken)
                ?? throw new NotFoundException("PurchaseOrder", request.PurchaseOrderId.Value);

            if (order.SupplierId != request.SupplierId)
                throw new DomainException("The goods receipt's supplier does not match the purchase order's supplier.");
            if (order.WarehouseId != request.WarehouseId)
                throw new DomainException("The goods receipt's warehouse does not match the purchase order's warehouse.");
            if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Cancelled)
                throw new DomainException("Stock can only be received against an approved purchase order.");
        }

        // H6: receiving stock posts IN rows on the same material lots other
        // movements lock, so a receipt and a concurrent issue of the same
        // material can no longer interleave between check and write.
        var lockKeys = (request.Lines ?? new List<PurchaseReceiptLineInput>())
            .Select(l => StockLockKey.MaterialLot(request.WarehouseId, l.MaterialId))
            .Distinct()
            .ToList();

        await using (await _stockLock.AcquireManyAsync(lockKeys, cancellationToken))
        {

        var receiptNumber = await _numberGenerator.NextAsync(DocumentType.PurchaseReceipt, cancellationToken: cancellationToken);

        var receipt = new PurchaseReceipt(receiptNumber, request.ReceiptDate, request.SupplierId, request.WarehouseId,
            _currentUser.UserName, _currentUser.UserName, order?.Id,
            request.SupplierDocumentNumber, request.Notes);

        foreach (var line in request.Lines ?? new List<PurchaseReceiptLineInput>())
        {
            await PurchaseRules.EnsureMaterialExists(_db, line.MaterialId, cancellationToken);

            if (line.PurchaseOrderLineId.HasValue && order is null)
                throw new DomainException("A receipt line cannot reference a purchase order line when the receipt is not against an order.");

            receipt.AddLine(line.MaterialId, line.Quantity, line.Unit, line.UnitCost, line.PurchaseOrderLineId, line.Notes);
        }

        _db.PurchaseReceipts.Add(receipt);

        // Stock: one IN row per received line on the append-only material ledger.
        // Balances are always a live sum over these rows - never an edited field.
        foreach (var line in receipt.Lines)
        {
            _db.MaterialTransactions.Add(new MaterialTransaction(
                DocumentType.PurchaseReceipt, receipt.ReceiptNumber, receipt.Id,
                request.ReceiptDate, line.MaterialId, request.WarehouseId, productionOrderId: null,
                quantity: line.Quantity, direction: MaterialTransactionDirection.In,
                unitCost: line.UnitCost, createdBy: _currentUser.UserName));
        }

        // Order progress: accumulate the received quantity on the order line so a
        // partial delivery can never present itself as complete.
        if (order is not null)
        {
            foreach (var line in receipt.Lines)
            {
                if (!line.PurchaseOrderLineId.HasValue) continue;
                order.RecordReceivedQuantity(line.PurchaseOrderLineId.Value, line.Quantity, line.Unit, _currentUser.UserName);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await _mediator.Send(new GetPurchaseReceiptByIdQuery(receipt.Id), cancellationToken);
        }
    }
}
