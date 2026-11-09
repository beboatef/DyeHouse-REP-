using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;

namespace DyeHouseERP.Application.Inventory;

/// <summary>
/// What a ledger row MEANS - the single vocabulary shared by every reader of the stock ledgers (the
/// traceability screen, its export, the printed lot card and the per-warehouse item card).
///
/// The classification is the movement's OWN document type and direction, never a screen's assumption,
/// and a compensating row stays a reversal of its original. Extracted here so two readers can never
/// disagree about what the same ledger row was.
/// </summary>
public static class StockMovementKinds
{
    /// <summary>A row of the customer-owned fabric ledger in the RAW material warehouse.</summary>
    public static string Raw(InventoryTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.RawReceiptMessage => t.Direction == TransactionDirection.In ? "Received" : "Returned",
        DocumentType.ProductionOrder => t.Direction == TransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.RawIssueExternalRelease => t.Direction == TransactionDirection.Out ? "IssuedExternal" : "Returned",
        DocumentType.CustomerReturn => "Returned",
        DocumentType.ReadyGoodsRawReturn => "Returned",
        DocumentType.ProductionOrderPause => "Returned",
        DocumentType.CustomerTransfer => t.Direction == TransactionDirection.In ? "TransferredIn" : "TransferredOut",
        DocumentType.StockAdjustment => t.Direction == TransactionDirection.In ? "AdjustedIn" : "AdjustedOut",
        _ => t.Direction == TransactionDirection.In ? "In" : "Out"
    };

    /// <summary>A row of the same fabric ledger in the READY goods warehouse.</summary>
    public static string Ready(InventoryTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.ReadyGoodsTransfer => t.Direction == TransactionDirection.In ? "Produced" : "TransferredOut",
        DocumentType.Delivery => t.Direction == TransactionDirection.Out ? "Delivered" : "Reversal",
        DocumentType.ReadyGoodsRawReturn => "Returned",
        DocumentType.StockAdjustment => t.Direction == TransactionDirection.In ? "AdjustedIn" : "AdjustedOut",
        _ => t.Direction == TransactionDirection.In ? "In" : "Out"
    };

    /// <summary>A row of the factory-owned material / operating-supplies ledger.</summary>
    public static string Material(MaterialTransaction t) => t.SourceDocumentType switch
    {
        DocumentType.PurchaseReceipt => "Received",
        DocumentType.MaterialIssue => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.SupplyIssue => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.MaterialSale => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Returned",
        DocumentType.MaterialTransfer => t.Direction == MaterialTransactionDirection.Out ? "TransferredOut" : "TransferredIn",
        DocumentType.PreparationDilution => t.Direction == MaterialTransactionDirection.Out ? "Issued" : "Received",
        _ => t.Direction == MaterialTransactionDirection.In ? "Received" : "Issued"
    };
}
