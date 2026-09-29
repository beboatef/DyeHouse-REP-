using System.ComponentModel.DataAnnotations;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Exceptions;

namespace DyeHouseERP.Domain.Entities;

/// <summary>
/// Transfers completed production output into the Ready Goods warehouse
/// (spec section 29 - "ترحيل"). Validated against the order's stage
/// completion before posting; the ledger row this creates (WarehouseId =
/// ready warehouse, RawMessageId = null, ProductionOrderId set) is what
/// both "production output" and "ready warehouse receipt" resolve to.
///
/// Posting happens at creation (the transfer and its InventoryTransaction
/// IN row commit together), so after creation the document is treated the
/// same way as every other stock-posting document in the system: quantities
/// are immutable, and corrections go through Cancel, which posts equal and
/// opposite reversal rows - the ledger is append-only and never drifts
/// from the document (spec sections 18, 30, 31).
/// </summary>
public class ReadyGoodsTransfer : AuditableEntity
{
    public string TransferNumber { get; private set; } = string.Empty;
    public DateTime TransferDate { get; private set; }
    public Guid ProductionOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal? QuantityKg { get; private set; }
    public decimal? QuantityMeter { get; private set; }
    public int? PieceCount { get; private set; }
    public string? Notes { get; private set; }
    public ReadyGoodsTransferStatus Status { get; private set; } = ReadyGoodsTransferStatus.Posted;

    /// <summary>
    /// R3: optimistic concurrency token, maintained by SQL Server. The cancel
    /// path (A2) posts reversal rows from this document, so a cancel racing a
    /// second cancel must fail at the database instead of double-reversing.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private ReadyGoodsTransfer() { } // EF Core

    public ReadyGoodsTransfer(
        string transferNumber, DateTime transferDate, Guid productionOrderId, Guid customerId, Guid itemId,
        Guid warehouseId, decimal? quantityKg, decimal? quantityMeter, string createdBy,
        int? pieceCount = null, string? notes = null)
    {
        if (quantityKg is null && quantityMeter is null)
            throw new ArgumentException("A ready goods transfer must specify a KG and/or Meter quantity.");

        TransferNumber = transferNumber;
        TransferDate = transferDate;
        ProductionOrderId = productionOrderId;
        CustomerId = customerId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        QuantityKg = quantityKg;
        QuantityMeter = quantityMeter;
        PieceCount = pieceCount;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Corrects only the header fields the ledger never depends on. Stock
    /// quantities are deliberately NOT editable here: the transfer's ledger
    /// rows are append-only, so changing quantities would split the document
    /// from the stock balance it created (spec sections 18, 30). A wrong
    /// quantity is corrected by cancelling the transfer - which posts reversal
    /// rows - and creating a new one.
    /// </summary>
    public void UpdateDetails(
        DateTime transferDate,
        int? pieceCount,
        string? notes)
    {
        TransferDate = transferDate;
        PieceCount = pieceCount;
        Notes = notes;
    }

    /// <summary>
    /// Cancellation reason is recorded in the notes (same convention as
    /// Invoice.Cancel) and the status flips to Cancelled; the reversal
    /// ledger rows are written by the command handler against the exact
    /// rows this transfer originally created.
    /// </summary>
    public void Cancel(string reason, string cancelledBy)
    {
        if (Status == ReadyGoodsTransferStatus.Cancelled)
            throw new DomainException($"Transfer '{TransferNumber}' is already cancelled.");

        Status = ReadyGoodsTransferStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"[Cancelled] {reason}" : $"{Notes}\n[Cancelled] {reason}";
        ModifiedBy = cancelledBy;
        ModifiedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// A transfer is created and posted in the same atomic step (the handler
/// writes the transfer and its ledger IN row together), so unlike Invoice
/// there is no Draft state - it starts Posted and can only be Cancelled.
/// </summary>
public enum ReadyGoodsTransferStatus
{
    Posted = 1,
    Cancelled = 2
}
