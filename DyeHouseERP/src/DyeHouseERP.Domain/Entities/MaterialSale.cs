using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Sale of FACTORY-OWNED materials/chemicals to a third party (spec section
/// 26). This is intentionally a different document from a processing
/// (job-work) Invoice: a job-work invoice bills processing a CUSTOMER's raw
/// material, while a material sale transfers ownership of stock the factory
/// bought. Keeping them apart means customer-owned raw material can never be
/// accidentally billed as a sale, and material sales never appear as job-work
/// revenue in costing.
///
/// Posting is the only step with any effect, and it posts atomically:
///  - one OUT MaterialTransaction per line (stock leaves the materials store),
///  - one Debit CustomerLedgerEntry when the buyer is an existing customer
///    (a walk-in buyer is recorded by name only and settles immediately),
///  - one IN TreasuryTransaction when an account is given (cash on delivery).
/// Cancelling posts equal-and-opposite rows; nothing is deleted.
/// </summary>
public class MaterialSale : AuditableEntity
{
    public string SaleNumber { get; private set; } = string.Empty; // system-generated, e.g. "MSL-2026-000007"
    public DateTime SaleDate { get; private set; }
    public Guid WarehouseId { get; private set; }

    /// <summary>Optional: an existing customer account. Null for a walk-in third-party buyer.</summary>
    public Guid? CustomerId { get; private set; }
    public string BuyerName { get; private set; } = string.Empty;  // always kept, snapshotted at creation

    public Guid? TreasuryAccountId { get; private set; }           // set = paid immediately on posting
    public string? PaymentMethod { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Tax { get; private set; }
    public string? Notes { get; private set; }

    public MaterialSaleStatus Status { get; private set; } = MaterialSaleStatus.Draft;

    public string? PostedBy { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<MaterialSaleLine> _lines = new();
    public IReadOnlyCollection<MaterialSaleLine> Lines => _lines.AsReadOnly();

    private MaterialSale() { } // EF Core

    public MaterialSale(string saleNumber, DateTime saleDate, Guid warehouseId, string buyerName, string createdBy,
        Guid? customerId = null, Guid? treasuryAccountId = null, string? paymentMethod = null,
        decimal discount = 0, decimal tax = 0, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(saleNumber)) throw new ArgumentException("Sale number is required.", nameof(saleNumber));
        if (string.IsNullOrWhiteSpace(buyerName)) throw new ArgumentException("Buyer name is required.", nameof(buyerName));
        if (discount < 0) throw new ArgumentException("Discount cannot be negative.", nameof(discount));
        if (tax < 0) throw new ArgumentException("Tax cannot be negative.", nameof(tax));

        SaleNumber = saleNumber;
        SaleDate = saleDate;
        WarehouseId = warehouseId;
        BuyerName = buyerName.Trim();
        CustomerId = customerId;
        TreasuryAccountId = treasuryAccountId;
        PaymentMethod = paymentMethod;
        Discount = discount;
        Tax = tax;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public bool IsEditable => Status == MaterialSaleStatus.Draft;
    public decimal SubTotal => _lines.Sum(l => l.LineTotal);
    public decimal Total => SubTotal - Discount + Tax;

    public MaterialSaleLine AddLine(Guid materialId, decimal quantity, MaterialUnit unit, decimal unitPrice, string? description = null)
    {
        EnsureEditable();
        var line = new MaterialSaleLine(Id, materialId, quantity, unit, unitPrice, description);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        var line = _lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new DomainException("That line does not belong to this material sale.");
        _lines.Remove(line);
    }

    public void SetTerms(decimal discount, decimal tax, Guid? treasuryAccountId, string? paymentMethod, string modifiedBy)
    {
        EnsureEditable();
        if (discount < 0) throw new ArgumentException("Discount cannot be negative.", nameof(discount));
        if (tax < 0) throw new ArgumentException("Tax cannot be negative.", nameof(tax));

        Discount = discount;
        Tax = tax;
        TreasuryAccountId = treasuryAccountId;
        PaymentMethod = paymentMethod;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Post(string modifiedBy)
    {
        if (Status == MaterialSaleStatus.Posted) throw new DomainException("This material sale has already been posted.");
        EnsureNotCancelled();
        if (_lines.Count == 0) throw new DomainException("A material sale must have at least one line before it can be posted.");

        Status = MaterialSaleStatus.Posted;
        PostedBy = modifiedBy;
        PostedAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == MaterialSaleStatus.Cancelled) throw new DomainException("This material sale is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        Status = MaterialSaleStatus.Cancelled;
        CancelledBy = modifiedBy;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = reason.Trim();
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
        Lock();
    }

    private void EnsureEditable()
    {
        EnsureNotCancelled();
        if (Status != MaterialSaleStatus.Draft) throw new DocumentLockedException("Material Sale", SaleNumber);
    }

    private void EnsureNotCancelled()
    {
        if (Status == MaterialSaleStatus.Cancelled)
            throw new DomainException("A cancelled material sale cannot be changed.");
    }
}

public class MaterialSaleLine : BaseEntity
{
    public Guid MaterialSaleId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public MaterialUnit Unit { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal => Quantity * UnitPrice;
    public string? Description { get; private set; }

    private MaterialSaleLine() { } // EF Core

    public MaterialSaleLine(Guid materialSaleId, Guid materialId, decimal quantity, MaterialUnit unit, decimal unitPrice, string? description = null)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitPrice < 0) throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        MaterialSaleId = materialSaleId;
        MaterialId = materialId;
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        Description = description;
    }
}
