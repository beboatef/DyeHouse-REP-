namespace DyeHouseERP.Domain.Enums;

/// <summary>Classification of customer-owned output held in the ready-goods warehouse. Waste is a production loss, never deliverable stock.</summary>
public enum ReadyGoodsCategory
{
    FirstGrade = 1,
    SecondGrade = 2,
    Separates = 3,
    Waste = 4
}


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
    Closed = 4,

    /// <summary>
    /// The receipt was received in error (wrong customer, item or warehouse) and has been
    /// cancelled through the permission-protected correction workflow. The document and its
    /// original IN ledger rows are preserved; the reversal is an equal-and-opposite OUT row.
    /// </summary>
    Cancelled = 5
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
    PayrollRun = 23,

    // ---- Operating supplies internal issue (spec section 27) ----
    SupplyIssue = 24,

    // ---- Materials/chemicals sold to third parties (spec section 26) ----
    MaterialSale = 25,

    // ---- Customer returns of processed goods to the raw material warehouse (spec sections 32-33) ----
    CustomerReturn = 26,

    // ---- Release of unused raw material when a Job Order is paused (spec section 19) ----
    ProductionOrderPause = 27,

    // ---- Moving ready goods back into raw-material stock (rework / reclassification) ----
    // Its own type because the movement is its own document: it is never a side effect of a
    // delivery and it is never implied by anything else that happens in the Ready Goods module.
    ReadyGoodsRawReturn = 28,

    // ---- Sub-customer delivery permits (spec section 31) ----
    // A sub-customer permit draws from its OWN numbering sequence, so issuing one never consumes a
    // number from the primary DEL sequence and several sub-permits of one primary permit cannot
    // collide on the same counter. It posts no stock, so this type exists ONLY to key the numbering
    // engine - it is never written as a ledger or treasury source document type.
    SubDelivery = 29
}

/// <summary>
/// Who carries the cost of a Job Order (spec section 20's cost responsibility).
///
/// This is stored, never inferred: an ordinary Job Order is worked for the customer
/// and the customer carries its cost (the factory bills the processing), while a Job
/// Order raised on a returned/reworked batch may genuinely be on the FACTORY's own
/// account - a quality claim the factory absorbs. Treating either as a guess would
/// silently move money, so an order created from a customer return must state it
/// explicitly and there is no default for that workflow.
/// </summary>
public enum CostBearer
{
    /// <summary>على حساب العميل - the customer carries the cost; the work is billable.</summary>
    Customer = 1,

    /// <summary>على حساب الشركة - the factory absorbs the cost; the work is not billable.</summary>
    Company = 2
}

/// <summary>
/// Whose material a customer return actually is (spec sections 32-33). Recorded on the
/// return so the record says not only WHAT came back and from whom, but whether the
/// factory owns it: the factory's own material coming back must never be treated as
/// customer-owned stock, and vice versa.
/// </summary>
public enum ReturnOwnership
{
    /// <summary>مادة العميل - the customer's own material coming back (the ordinary case).</summary>
    CustomerOwned = 1,

    /// <summary>مادة الشركة - factory-owned material coming back (e.g. our own stock sent out and rejected).</summary>
    CompanyOwned = 2
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
    Cancelled = 5,

    /// <summary>
    /// موقوف مؤقتًا - paused, NOT cancelled (spec section 19).
    /// Unused raw material has been released back to the customer's raw stock and
    /// the order can still be resumed, which re-issues material and returns the
    /// order to التشكيل. A cancelled order cannot come back.
    /// </summary>
    Paused = 6
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
    // Phase 4: the legacy start/complete/skip workflow was deleted with its endpoint and its
    // AllowSkip column, so NO code path can move a stage execution here - an order simply never
    // runs an operation and moves on to whichever operation the operator picks next. The value is
    // kept (and never renumbered) so any historical row that carries it still reads back, and the
    // few places that mention it are defensive guards, not an active workflow.
    Skipped = 4
}

/// <summary>Why raw material is leaving the factory outside of normal internal production (spec section 14).</summary>
public enum RawReleaseReason
{
    ReturnToCustomer = 1,
    ExternalProcessing = 2,
    Sale = 3
}

/// <summary>
/// Progress of an External Processing release (spec section 21): material sent
/// to an outside processor, then received back. <see cref="NotApplicable"/> is
/// used for returns-to-customer and outright sales, which never come back.
/// </summary>
public enum ExternalProcessingStatus
{
    NotApplicable = 0,
    AwaitingReturn = 1,
    PartiallyReturned = 2,
    Returned = 3,
    Cancelled = 4
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
/// <summary>
/// Categories of an additional Job Order cost line (spec section 35).
///
/// "تشغيل خارجي" (external processing) is deliberately NOT a category here: that
/// cost is already DERIVED from the RawExternalRelease rows that recorded the
/// actual outside charge, so allowing it as a hand-entered cost entry as well
/// would double-count the same money. The categories below are the ones the spec
/// expects a user to add manually.
///
/// Stored as a string (nvarchar) by CostEntryConfiguration, so adding members
/// here is not a schema change.
/// </summary>
public enum CostCategory
{
    Labor = 1,
    Electricity = 2,
    Fuel = 3,
    Maintenance = 4,
    Other = 5,

    /// <summary>نقل - transport / freight.</summary>
    Transport = 6,

    /// <summary>تعبئة - packaging.</summary>
    Packaging = 7,

    /// <summary>إصلاح - repairs.</summary>
    Repair = 8
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

/// <summary>
/// Lifecycle of a monthly payroll run (spec sections 36 and 39):
/// Draft -&gt; Calculated -&gt; UnderReview -&gt; Approved -&gt; Posted, with a posted run
/// REVERSED (never edited) by a linked adjustment run. Cancelling is the other
/// terminal state; a reversed run can never be cancelled. Posting is the only step
/// with a financial effect.
/// </summary>
public enum PayrollRunStatus
{
    Draft = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4,
    Calculated = 5,
    UnderReview = 6,
    Reversed = 7
}

/// <summary>
/// Distinguishes the two FACTORY-owned stores (spec sections 23 + 27):
/// production chemicals/dyes go to the materials store, while spare parts,
/// packaging and maintenance consumables go to the separate operating
/// supplies store. Both live in the same Material ledger - only the store,
/// the internal-issue workflow and the reporting differ.
/// </summary>
public enum MaterialKind
{
    Chemical = 1,
    OperatingSupply = 2
}

/// <summary>Internal recipients of operating supplies (spec section 27).</summary>
public enum SupplyIssueStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

/// <summary>Lifecycle of a factory-owned materials sale (spec section 26). Posting is the only step with a stock/financial effect.</summary>
public enum MaterialSaleStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3
}

/// <summary>
/// Lifecycle of a material issue charged to a Production Order (spec section 27).
/// A material issue is a stock posting the moment it is created - there is no draft
/// step - so cancellation only ever reverses an already-posted document with a
/// compensating IN row rather than deleting the OUT row.
/// </summary>
public enum MaterialIssueStatus
{
    Posted = 1,
    Cancelled = 2
}
/// <summary>
/// The reason a user requested a second user to authorise an action (spec section 24).
/// Each value maps to a controller/area so approvals, permission checks and the
/// approval centre can route, filter and describe the request by what it is about.
/// </summary>
public enum ApprovalRequestType
{
    /// <summary>
    /// RETIRED - allocating raw material to an EXISTING Job Order (a reserved quantity with no actual issue).
    /// The operation was removed from the system: the raw deduction is made exactly once, by the raw issue
    /// that creates the order (<see cref="RawIssue"/>), which also records the single allocation row. The
    /// member is deliberately NOT renumbered or deleted, because rows already stored under it (a pending or
    /// decided request from before the removal) must keep reading back exactly as they were recorded. Any
    /// attempt to SUBMIT or EXECUTE one now explains why instead of failing cryptically - it can only be
    /// rejected. The numeric value is therefore frozen forever. (No [Obsolete] attribute: the member is
    /// still legitimately READ when historical rows are listed, so marking it would only produce noise.)
    /// </summary>
    AllocateRaw = 1,
    ResumeProduction = 2,
    CustomerTransfer = 3,
    RawExternalRelease = 4,
    StockAdjustment = 5,

    /// <summary>
    /// The canonical raw issue that creates the Job Order (spec section 5): issuing more
    /// material than the customer owns on the lot needs the same single-use, different-user
    /// authorisation every other negative-stock exception does.
    /// </summary>
    RawIssue = 6
}

/// <summary>Lifecycle of an approval request (spec section 24).</summary>
public enum ApprovalRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>
/// Kind of pay component (spec section 35): how it is reported in the wage bill and
/// which payroll totals it feeds. Stored as a string by design, but kept as a strong
/// enum in code so the UI, commands and queries can never invent a value.
/// </summary>
public enum PayComponentKind
{
    Allowance = 1,
    Incentive = 2,
    Bonus = 3,
    Commission = 4,
    Deduction = 5
}

/// <summary>How a pay component is computed (spec section 35). Stored as a string by design.</summary>
public enum PayComponentCalculation
{
    FixedAmount = 1,
    PercentOfBasic = 2,
    PerAttendanceDay = 3,
    PerOvertimeHour = 4,
    PercentOfGross = 5
}

/// <summary>Statutory contribution kind (spec section 36).</summary>
public enum ContributionKind
{
    Tax = 1,
    Insurance = 2
}

/// <summary>Payroll base on which a contribution is calculated (spec section 36).</summary>
public enum ContributionBase
{
    BasicSalary = 1,
    GrossPay = 2
}

/// <summary>Payroll accounting destination kind (spec section 37). Stored as a string by design.</summary>
public enum PayrollAccountKind
{
    BasicSalary = 1,
    Allowance = 2,
    Deduction = 3,
    Tax = 4,
    Insurance = 5,
    Advance = 6,
    NetPay = 7
}

/// <summary>Attendance status for one employee on one day (spec sections 30-31).</summary>
public enum AttendanceStatus
{
    Present = 1,
    Late = 2,
    Absent = 3,
    OnLeave = 4,
    RestDay = 5,
    /// <summary>
    /// A public holiday (spec section 31) - a non-working day that is neither the
    /// weekly rest day nor leave. Recorded on the same non-working path so a
    /// holiday is never mistaken for an absence.
    /// </summary>
    Holiday = 6
}

/// <summary>How an attendance row was created (spec sections 30-31).</summary>
public enum AttendanceSource
{
    Manual = 1
}

/// <summary>Lifecycle of a leave request (spec section 32).</summary>
public enum LeaveStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>Lifecycle of an overtime entry (spec section 33).</summary>
public enum OvertimeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>Overtime category that selects the configured multiplier (spec section 33).</summary>
public enum OvertimeCategory
{
    Normal = 1,
    Weekend = 2,
    Holiday = 3
}

/// <summary>Lifecycle of an employee advance (spec section 34).</summary>
public enum AdvanceStatus
{
    Active = 1,
    Closed = 2,
    Cancelled = 3
}
