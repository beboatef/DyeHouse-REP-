namespace DyeHouseERP.Domain.Enums;

/// <summary>
/// The two independent base units supported by the system (spec section 5).
/// There is deliberately NO "Top/توب" unit and NO fixed KG<->Meter conversion.
/// </summary>
public enum UnitOfMeasure
{
    KG = 1,
    Meter = 2
}

/// <summary>Raw receiving inspection result (spec section 11).</summary>
public enum InspectionStatus
{
    PendingInspection = 1,
    Accepted = 2,
    AcceptedWithNotes = 3,
    Rejected = 4
}

/// <summary>Lifecycle status of a raw receipt/message (spec section 10).</summary>
public enum RawMessageStatus
{
    Open = 1,        // still has un-allocated balance
    PartiallyUsed = 2,
    Depleted = 3,
    Closed = 4
}

/// <summary>
/// The logical document types that go through the automatic numbering engine
/// (spec section 6). Each has its own independent sequence.
/// </summary>
public enum DocumentType
{
    RawReceiptMessage = 1,
    ProductionOrder = 2,
    RawIssueExternalRelease = 3,
    RawReturn = 4,
    CustomerTransfer = 5,
    StockAdjustment = 6,
    Delivery = 7,
    Invoice = 8,
    Receipt = 9,
    Payment = 10,
    TreasuryTransfer = 11,
    MaterialTransfer = 12,
    MaterialIssue = 13,
    PreparationDilution = 14,
    ReadyGoodsTransfer = 15,
    ProductionRequest = 16
}

/// <summary>Overall lifecycle of a Production Order (spec section 12).</summary>
public enum ProductionOrderStatus
{
    Draft = 1,           // created, no raw allocated yet
    RawAllocated = 2,    // raw material allocated from message(s), not yet started
    InProduction = 3,    // at least one stage started
    Completed = 4,       // all required stages completed / transferred to ready goods
    Cancelled = 5
}

public enum ProductionPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4
}

/// <summary>Status of one stage's execution within one Production Order (spec sections 19-21).</summary>
public enum StageExecutionStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Skipped = 4     // only allowed if the stage definition has AllowSkip = true
}

/// <summary>Why raw material is leaving the factory outside of normal internal production (spec section 14).</summary>
public enum RawReleaseReason
{
    ReturnToCustomer = 1,
    ExternalProcessing = 2,
    Sale = 3
}

/// <summary>Direction of a manual stock adjustment (spec section 16).</summary>
public enum AdjustmentType
{
    Increase = 1,
    Decrease = 2
}

/// <summary>Lifecycle of a Separate - reprocessable material split off during a stage (spec section 22).</summary>
public enum SeparateStatus
{
    PendingReprocessing = 1,
    Reprocessing = 2,
    Reprocessed = 3,
    Ready = 4,
    Scrapped = 5
}

/// <summary>Units supported for materials/chemicals (spec section 24) - a separate set from UnitOfMeasure, which is fabric-only (KG/Meter).</summary>
public enum MaterialUnit
{
    KG = 1,
    Gram = 2,
    Liter = 3
}

/// <summary>Direction of a MaterialTransaction ledger row - mirrors TransactionDirection but kept distinct since materials are factory-owned, not customer-owned.</summary>
public enum MaterialTransactionDirection
{
    In = 1,
    Out = -1
}

/// <summary>Delivery document lifecycle (spec section 31).</summary>
public enum DeliveryStatus
{
    Draft = 1,
    Prepared = 2,
    Delivered = 3,
    Cancelled = 4
}

/// <summary>Invoice lifecycle (spec section 32).</summary>
public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

/// <summary>Operating cost categories assignable to a Production Order (spec section 35) - Materials and Preparation costs are derived from MaterialIssue/MaterialPreparation instead of this, to avoid double counting.</summary>
public enum CostCategory
{
    Labor = 1,
    Electricity = 2,
    Fuel = 3,
    Maintenance = 4,
    Other = 5
}

/// <summary>Lifecycle of a customer-submitted processing request (spec section 36) before it becomes an internal Production Order.</summary>
public enum ProductionRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    ConvertedToOrder = 4
}
