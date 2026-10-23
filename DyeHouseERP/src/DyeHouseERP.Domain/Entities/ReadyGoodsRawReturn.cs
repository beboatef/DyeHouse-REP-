using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Moves finished (ready) stock back into raw-material stock - رجوع الجاهز إلى الخام for rework or
/// reclassification (spec section 29's stock destinations).
///
/// This document exists because the move is NOT allowed to happen any other way. Undelivered goods
/// stay in Ready Goods: nothing in the system ever pushes them back into raw material automatically,
/// and no delivery, cancellation, order completion or reprocessing reclassifies them as a side effect.
/// Returning stock to the raw side is therefore a request of its own - authorized under its own
/// permission, carrying a MANDATORY reason, traceable to the order it came from and the raw lot it
/// lands on, and audited like every other stock-posting document.
///
/// It posts two ledger rows in one transaction: an OUT on the ready lot (RawMessageId = null,
/// ProductionOrderId set - the same dimension a ready-goods transfer posts IN against) and an IN on
/// the destination raw lot. The ledger stays append-only: the original ready-goods IN row is never
/// edited or deleted.
/// </summary>
public class ReadyGoodsRawReturn : AuditableEntity
{
    public string MovementNumber { get; private set; } = string.Empty; // e.g. "RGR-2026-000003"
    public DateTime MovementDate { get; private set; }

    /// <summary>The Job Order whose ready-goods output is being reclassified. Never null.</summary>
    public Guid ProductionOrderId { get; private set; }

    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public ReadyGoodsCategory? Category { get; private set; }

    /// <summary>The Ready Goods warehouse the stock leaves - always a Ready Goods warehouse.</summary>
    public Guid SourceWarehouseId { get; private set; }

    /// <summary>The raw material lot the quantity lands back on - always a raw-material warehouse lot.</summary>
    public Guid DestinationRawMessageId { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }

    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }

    /// <summary>Mandatory: a reclassification of posted stock is never silent.</summary>
    public string Reason { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, maintained by SQL Server. The document posts stock, so two
    /// operations on the same document must not silently overwrite each other.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ReadyGoodsRawReturn() { } // EF Core

    public ReadyGoodsRawReturn(
        string movementNumber, DateTime movementDate, Guid productionOrderId, Guid customerId, Guid itemId,
        Guid sourceWarehouseId, Guid destinationRawMessageId, Guid destinationWarehouseId,
        decimal? quantityKg, decimal? quantityMeter, string reason, string createdBy, string? notes = null,
        ReadyGoodsCategory? category = null)
    {
        if (string.IsNullOrWhiteSpace(movementNumber))
            throw new ArgumentException("Movement number is required.", nameof(movementNumber));
        if (productionOrderId == Guid.Empty)
            throw new ArgumentException("Job Order is required.", nameof(productionOrderId));
        if (sourceWarehouseId == Guid.Empty)
            throw new ArgumentException("The source Ready Goods warehouse is required.", nameof(sourceWarehouseId));
        if (destinationRawMessageId == Guid.Empty)
            throw new ArgumentException(
                "The raw material lot the stock returns to is required.", nameof(destinationRawMessageId));
        if (sourceWarehouseId == destinationWarehouseId)
            throw new DomainException(
                "A ready-goods-to-raw move must leave the Ready Goods warehouse and land on a raw material lot; the two warehouses cannot be the same one.");
        if (quantityKg is null && quantityMeter is null)
            throw new DomainException("Specify the moved quantity in KG and/or Meter.");
        if (quantityKg is <= 0 || quantityMeter is <= 0)
            throw new DomainException("A moved quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(
                "A reason is required to move ready goods back into raw material stock. نقل مخزون جاهز إلى الخام يتطلب سببًا.");

        MovementNumber = movementNumber;
        MovementDate = movementDate;
        ProductionOrderId = productionOrderId;
        CustomerId = customerId;
        ItemId = itemId;
        Category = category;
        SourceWarehouseId = sourceWarehouseId;
        DestinationRawMessageId = destinationRawMessageId;
        DestinationWarehouseId = destinationWarehouseId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        Reason = reason.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;

        // Finalized the moment it posts, exactly like every other stock-posting document here: the
        // correction is a new, opposite movement, never an edit of this one.
        Lock();
    }
}
