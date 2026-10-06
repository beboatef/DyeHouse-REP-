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
        string? reason = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(returnNumber))
            throw new ArgumentException("Customer return number is required.", nameof(returnNumber));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));

        ReturnNumber = returnNumber;
        ReturnDate = returnDate;
        CustomerId = customerId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
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
