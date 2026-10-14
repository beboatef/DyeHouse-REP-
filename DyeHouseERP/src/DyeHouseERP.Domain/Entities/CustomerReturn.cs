using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Customer return of processed goods (spec sections 32-33).
///
/// Returned material goes back to the RAW MATERIAL warehouse - never to Ready
/// Goods - because it is customer-owned raw stock again the moment it comes
/// back through the gate. Posting the return creates an IN ledger row on the
/// chosen raw lot, which is what makes it available for a later Job Order
/// (spec section 33) without anybody editing a displayed balance.
///
/// The return reason is OPTIONAL free text, there is deliberately no
/// condition/grade field, and a single return may cover several Job
/// Orders/basins. When the originating Job Order is unknown the caller must
/// still name the target raw lot explicitly (see the command handler): the
/// ledger moves stock between lots, so "some somewhere" is not a state this
/// document can represent.
/// </summary>
public class CustomerReturn : AuditableEntity
{
    public string ReturnNumber { get; private set; } = string.Empty; // system-generated, e.g. "CRT-2026-000014"
    public DateTime ReturnDate { get; private set; }
    public Guid CustomerId { get; private set; }

    /// <summary>Optional free text - the spec's return reason is genuinely optional, so no reason is required.</summary>
    public string? Reason { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// The document the goods are coming back against - an invoice number, a delivery (إذن تسليم)
    /// number, or the customer's own reference (spec section 32's "reference document"). Free text on
    /// purpose: the goods may come back against a document this system did not issue. Recorded only -
    /// it never drives a financial effect (a return changes no invoice and creates no credit note).
    /// </summary>
    public string? ReferenceDocument { get; private set; }

    /// <summary>
    /// Whose material came back (spec section 32's "ownership"). The ordinary case is the customer's
    /// own material, which is why that is the default; factory-owned material coming back is recorded
    /// explicitly so it can never be counted as customer stock.
    /// </summary>
    public ReturnOwnership Ownership { get; private set; } = ReturnOwnership.CustomerOwned;

    // ---- Cancellation / correction audit (spec sections 32-33 + 49) ----
    // A return entered by mistake (wrong lot, wrong quantity, wrong customer) is corrected by
    // CANCELLING it: the document and its IN ledger rows stay readable, and the stock it restored
    // is taken back out with an equal-and-opposite OUT row. These fields record who, when and why.
    public bool IsCancelled { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// H1 optimistic concurrency token, maintained by SQL Server. A return posts
    /// ledger rows, so two concurrent operations on the same document must not
    /// silently overwrite each other.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<CustomerReturnLine> _lines = new();
    public IReadOnlyCollection<CustomerReturnLine> Lines => _lines.AsReadOnly();

    private CustomerReturn() { } // EF Core

    public CustomerReturn(
        string returnNumber, DateTime returnDate, Guid customerId, string createdBy,
        string? reason = null, string? notes = null,
        string? referenceDocument = null, ReturnOwnership ownership = ReturnOwnership.CustomerOwned)
    {
        if (string.IsNullOrWhiteSpace(returnNumber))
            throw new ArgumentException("Customer return number is required.", nameof(returnNumber));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));

        ReturnNumber = returnNumber;
        ReturnDate = returnDate;
        CustomerId = customerId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ReferenceDocument = string.IsNullOrWhiteSpace(referenceDocument) ? null : referenceDocument.Trim();
        Ownership = ownership;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds one returned line. <paramref name="rawMessageId"/> is the raw lot the
    /// quantity lands in and is always populated by the time the line exists -
    /// the command handler resolves it from the Job Order when the caller omitted
    /// it, or requires it outright when the Job Order is unknown.
    /// </summary>
    public CustomerReturnLine AddLine(
        Guid rawMessageId, Guid itemId, decimal? quantityKg, decimal? quantityMeter,
        Guid? productionOrderId = null, Guid? formationGroupId = null, string? notes = null)
    {
        // The document locks itself the moment it posts (see Post), so its ledger rows stay
        // append-only: a line can never be added to a return that already posted stock. The
        // correction path is the audited cancellation, not an edit.
        if (IsLocked)
            throw new DocumentLockedException("Customer Return", ReturnNumber);

        if (rawMessageId == Guid.Empty)
            throw new DomainException("A customer return line must name the raw material lot it returns into.");
        if (itemId == Guid.Empty)
            throw new DomainException("A customer return line must name an item.");
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A customer return line must specify a KG and/or Meter quantity.");
        if (quantityKg is <= 0 || quantityMeter is <= 0)
            throw new DomainException("A customer return line quantity must be greater than zero.");

        var line = new CustomerReturnLine(
            Id, rawMessageId, itemId, quantityKg, quantityMeter, productionOrderId, formationGroupId, notes);
        _lines.Add(line);
        return line;
    }

    /// <summary>
    /// Locks the document the moment it posts, exactly like every other
    /// stock-posting document here: its ledger rows are append-only, so a wrong
    /// return is corrected with a compensating movement rather than an edit.
    /// </summary>
    public void Post() => Lock();

    /// <summary>
    /// Cancels a posted return. The caller writes the compensating OUT ledger rows and has already
    /// proved that the returned stock is still on its lot - this method only enforces the invariants
    /// it owns: a reason is mandatory, and a second cancellation is refused.
    /// </summary>
    public void Cancel(string reason, string modifiedBy)
    {
        if (IsCancelled)
            throw new DomainException($"Customer return {ReturnNumber} is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");

        IsCancelled = true;
        CancellationReason = reason.Trim();
        CancelledBy = modifiedBy;
        CancelledAtUtc = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

public class CustomerReturnLine : BaseEntity
{
    public Guid CustomerReturnId { get; private set; }

    /// <summary>The raw lot the returned quantity is added back to. Never empty.</summary>
    public Guid RawMessageId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>The Job Order the goods came from, when it is known. Optional by design (spec section 32).</summary>
    public Guid? ProductionOrderId { get; private set; }

    /// <summary>The formation basin the goods came from, when it is known. Optional.</summary>
    public Guid? FormationGroupId { get; private set; }

    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public string? Notes { get; private set; }

    private CustomerReturnLine() { } // EF Core

    internal CustomerReturnLine(
        Guid customerReturnId, Guid rawMessageId, Guid itemId,
        decimal? quantityKg, decimal? quantityMeter,
        Guid? productionOrderId, Guid? formationGroupId, string? notes)
    {
        CustomerReturnId = customerReturnId;
        RawMessageId = rawMessageId;
        ItemId = itemId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        ProductionOrderId = productionOrderId;
        FormationGroupId = formationGroupId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
