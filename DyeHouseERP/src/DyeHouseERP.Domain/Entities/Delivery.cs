using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Delivery document (spec section 31) - may cover several Production
/// Orders for the same customer. Has no financial value by itself; an
/// Invoice is a separate document that may reference a Delivery.
/// </summary>
public class Delivery : AuditableEntity
{
    public string DeliveryNumber { get; private set; } = string.Empty;
    public DateTime DeliveryDate { get; private set; }
    public Guid CustomerId { get; private set; }
    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Draft;
    public string? Notes { get; private set; }

    private readonly List<DeliveryLine> _lines = new();
    public IReadOnlyCollection<DeliveryLine> Lines => _lines.AsReadOnly();

    private Delivery() { } // EF Core

    public Delivery(string deliveryNumber, DateTime deliveryDate, Guid customerId, string createdBy, string? notes = null)
    {
        DeliveryNumber = deliveryNumber;
        DeliveryDate = deliveryDate;
        CustomerId = customerId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public DeliveryLine AddLine(Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes)
    {
        if (Status != DeliveryStatus.Draft)
            throw new DocumentLockedException("Delivery", DeliveryNumber);
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("A delivery line must specify a KG and/or Meter quantity.");

        var line = new DeliveryLine(Id, productionOrderId, itemId, color, quantityKg, quantityMeter, pieceCount, rawOrigin, notes);
        _lines.Add(line);
        return line;
    }

    public void MarkPrepared()
    {
        if (Status != DeliveryStatus.Draft) throw new DomainException($"Delivery is {Status}, expected Draft.");
        if (_lines.Count == 0) throw new DomainException("Cannot prepare a delivery with no lines.");
        Status = DeliveryStatus.Prepared;
    }

    /// <summary>
    /// Marks Delivered. Ledger balance validation and posting the OUT
    /// transactions happen in the application handler (needs ledger
    /// access) - this just guards the state transition and then locks the
    /// document from further normal editing (spec: "lock the document from
    /// normal editing" after Delivered).
    /// </summary>
    public void MarkDelivered()
    {
        if (Status != DeliveryStatus.Prepared) throw new DomainException($"Delivery is {Status}, expected Prepared.");
        Status = DeliveryStatus.Delivered;
        Lock();
    }

    public void Cancel(string reason, string modifiedBy)
    {
        if (Status == DeliveryStatus.Cancelled) throw new DomainException("Delivery is already cancelled.");
        Status = DeliveryStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled] {reason}" : $"{Notes}\n[Cancelled] {reason}";
        ModifiedBy = modifiedBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

public class DeliveryLine : BaseEntity
{
    public Guid DeliveryId { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Color { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public int? PieceCount { get; private set; }
    public string? RawOrigin { get; private set; }
    public string? Notes { get; private set; }

    private DeliveryLine() { } // EF Core

    internal DeliveryLine(Guid deliveryId, Guid productionOrderId, Guid itemId, string? color,
        decimal? quantityKg, decimal? quantityMeter, int? pieceCount, string? rawOrigin, string? notes)
    {
        DeliveryId = deliveryId;
        ProductionOrderId = productionOrderId;
        ItemId = itemId;
        Color = color;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        RawOrigin = rawOrigin;
        Notes = notes;
    }
}
