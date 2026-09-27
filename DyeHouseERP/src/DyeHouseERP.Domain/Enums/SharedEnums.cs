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
    ProductionRequest = 16,
    FormationRequest = 17,

    /// <summary>
    /// The checks register. A check's own number comes from the bank, not from
    /// this numbering engine, so no sequence is seeded for this type - it exists
    /// purely so cash/bank ledger rows posted by a cleared check can name their
    /// real source document (spec sections 40 and 46).
    /// </summary>
    CheckRegister = 18,

    // ---- Purchases (spec section 35) ----
    PurchaseOrder = 19,
    PurchaseReceipt = 20,
    SupplierInvoice = 21,
    SupplierPayment = 22,

    // ---- Payroll (spec section 36) ----
    PayrollRun = 23
}

/// <summary>
/// The two job-order line types of a dyehouse (spec section 16). Stored as
/// real data (never inferred from text) so it can be filtered, reported on,
/// costed and traced. "Closed line" = one continuous line processed as a
/// whole; "open line" (على المفتوح) = processed as open-width / unclosed
/// fabric. No behaviour is hard-coded off it - it is a first-class property
/// that flows through filters, reports, costing and traceability.
/// </summary>
public enum JobOrderType
{
    ClosedLine = 1,
    OpenLine = 2
}

/// <summary>Lifecycle of a Formation Request (طلب تشكيل) - spec section 32.</summary>
public enum FormationRequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    InProgress = 4,
    PartiallyCompleted = 5,
    Completed = 6,
    Rejected = 7,
    Cancelled = 8
}

/// <summary>Direction of a check instrument (spec section 37): a customer check we received, or one we issued/endorsed to a supplier.</summary>
public enum CheckDirection
{
    CustomerCheck = 1,
    SupplierCheck = 2
}

/// <summary>Check lifecycle (spec section 38). A check is ONE physical financial instrument; every transition is a recorded movement, never a new check.</summary>
public enum CheckStatus
{
    Received = 1,
    InHand = 2,
    Deposited = 3,
    Endorsed = 4,
    Cleared = 5,
    Bounced = 6,
    Cancelled = 7
}

/// <summary>Who physically holds the check right now.</summary>
public enum CheckHolderType
{
    Customer = 1,
    Company = 2,
    Supplier = 3,
    Bank = 4
}

/// <summary>Every way a check can move (spec section 39) - each one appends a CheckMovement row, so the instrument's full history is always reconstructable.</summary>
public enum CheckMovementType
{
    Received = 1,
    Issued = 2,
    ReturnedToHolder = 3,
    Endorsed = 4,
    Deposited = 5,
    Cleared = 6,
    Bounced = 7,
    Cancelled = 8
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

/// <summary>Lifecycle of a purchase order (spec section 35). Receiving is what moves it towards Received - never a manual status edit.</summary>
public enum PurchaseOrderStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    PartiallyReceived = 4,
    Received = 5,
    Cancelled = 6
}

/// <summary>Lifecycle of a supplier invoice. Posting is the moment it becomes a payable on the supplier account; cancelling posts a reversal.</summary>
public enum SupplierInvoiceStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

/// <summary>Employee active state (spec section 36).</summary>
public enum EmployeeStatus
{
    Active = 1,
    Suspended = 2,
    Terminated = 3
}

/// <summary>Lifecycle of a monthly payroll run (spec section 36). Posting is the only step with a financial effect.</summary>
public enum PayrollRunStatus
{
    Draft = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4
}
