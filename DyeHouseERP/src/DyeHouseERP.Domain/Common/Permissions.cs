namespace DyeHouseERP.Domain.Common;

/// <summary>
/// The full granular permission list from spec section 41. Each string is
/// carried as a role claim on the JWT (see JwtTokenService) and checked
/// server-side via PermissionRequirement/PermissionAuthorizationHandler -
/// hiding a button in the frontend is never the enforcement point.
/// A user holding the "admin" role passes every permission check (see
/// PermissionAuthorizationHandler) without needing every string listed
/// individually.
/// </summary>
public static class Permissions
{
    public const string Admin = "admin";

    public const string CustomersView = "customers.view";
    public const string CustomersCreate = "customers.create";
    public const string CustomersEdit = "customers.edit";

    public const string ItemsView = "items.view";
    public const string ItemsCreate = "items.create";
    public const string ItemsEdit = "items.edit";

    public const string RawReceive = "raw.receive";
    public const string RawInspect = "raw.inspect";
    /// <summary>
    /// Cancelling a raw receipt that was entered in error (wrong customer, item or warehouse).
    /// A distinct right from <see cref="RawReceive"/>: receiving is daily work, whereas reversing a
    /// posted stock document is a correction and must never be implied by the ability to receive.
    /// </summary>
    public const string RawReceiptCancel = "raw.receipt_cancel";
    public const string RawConsume = "raw.consume";
    public const string RawReturn = "raw.return";
    /// <summary>
    /// Cancelling a posted customer return (a correction that takes the returned stock back out of the
    /// lot). A distinct right from <see cref="RawReturn"/>: recording a return is daily work, whereas
    /// reversing a posted stock document is a correction and must never be implied by it.
    /// </summary>
    public const string RawReturnCancel = "raw.return_cancel";
    public const string RawExternalRelease = "raw.external_release";
    public const string RawTransfer = "raw.transfer";

    public const string ProductionView = "production.view";
    public const string ProductionCreate = "production.create";
    public const string ProductionEdit = "production.edit";
    public const string ProductionExecuteStage = "production.execute_stage";
    public const string ProductionComplete = "production.complete";
    public const string ProductionReprocess = "production.reprocess";
    /// <summary>Resuming a paused Job Order (spec section 19) - a distinct right from ordinary stage work, because it re-issues raw material.</summary>
    public const string ProductionResume = "production.resume";
    /// <summary>Cancelling a material issue posted against a Job Order (spec section 27) - a reversal that puts chemicals back into stock, so it is its own right rather than part of ordinary stage work.</summary>
    public const string ProductionIssueMaterialCancel = "production.issue_material_cancel";
    /// <summary>Maintaining the machines master (spec section 20).</summary>
    public const string ProductionManageMachines = "production.manage_machines";
    /// <summary>Maintaining the production execution shifts master (spec section 19) - separate from the HR attendance shifts.</summary>
    public const string ProductionManageShifts = "production.manage_shifts";
    public const string InventoryView = "inventory.view";
    public const string InventoryEdit = "inventory.edit";
    public const string InventoryDelete = "inventory.delete";
    public const string InventoryAdjust = "inventory.adjust";
    /// <summary>Authorizes overriding the negative-stock block (spec section 18).</summary>
    public const string InventoryAllowNegativeStock = "inventory.allow_negative_stock";
    public const string InventoryApproveNegativeStock = "inventory.approve_negative_stock";

    public const string ReadyView = "ready.view";
    public const string ReadyTransfer = "ready.transfer";
    public const string ReadyDeliver = "ready.deliver";

    /// <summary>
    /// Authorizes correcting an ALREADY APPROVED (delivered) delivery
    /// (spec sections 30 + 49). Deliberately NOT part of <see cref="ReadyDeliver"/>:
    /// creating and posting a delivery is normal operation, whereas changing one
    /// after customers have been charged against it is a sensitive correction that
    /// moves stock back into Ready Goods and needs its own right.
    /// </summary>
    public const string ReadyEditPostApproval = "ready.edit_post_approval";

    public const string InvoicesView = "invoices.view";
    public const string InvoicesCreate = "invoices.create";
    /// <summary>
    /// Editing a DRAFT invoice's header and lines (spec section 32). Separate from
    /// <see cref="InvoicesCreate"/> so composing an invoice and revising someone else's draft are
    /// two configurable rights. Never applies to an issued invoice - that is corrected by reversal.
    /// </summary>
    public const string InvoicesEdit = "invoices.edit";
    public const string InvoicesIssue = "invoices.issue";
    public const string InvoicesCancel = "invoices.cancel";

    public const string TreasuryView = "treasury.view";
    public const string TreasuryCreate = "treasury.create";
    /// <summary>
    /// Cancelling/reversing a posted treasury document (receipt, payment, transfer). A distinct right from
    /// <see cref="TreasuryCreate"/>: recording a movement is daily work, whereas reversing a posted one is a
    /// correction that must never be implied by the ability to record it.
    /// </summary>
    public const string TreasuryCancel = "treasury.cancel";

    // ---- Formation Request / طلب تشكيل (spec sections 28-33) ----
    // Granular by action, not just by module: seeing a request, editing a
    // draft, approving it and converting it to a Job Order are four separate
    // rights, so a production planner can prepare requests without being able
    // to approve their own work.
    public const string FormationView = "formation.view";
    public const string FormationCreate = "formation.create";
    public const string FormationEdit = "formation.edit";
    public const string FormationSubmit = "formation.submit";
    public const string FormationApprove = "formation.approve";
    public const string FormationReject = "formation.reject";
    public const string FormationCancel = "formation.cancel";
    public const string FormationDelete = "formation.delete";
    public const string FormationManageGroups = "formation.manage_groups";
    public const string FormationManageSpecifications = "formation.manage_specifications";
    public const string FormationViewTraceability = "formation.traceability";
    public const string FormationPrint = "formation.print";
    public const string FormationExport = "formation.export";

    // ---- Checks register (spec sections 37-40) ----
    public const string ChecksView = "checks.view";
    public const string ChecksCreate = "checks.create";
    public const string ChecksEdit = "checks.edit";
    public const string ChecksEndorse = "checks.endorse";
    public const string ChecksDeposit = "checks.deposit";
    public const string ChecksClear = "checks.clear";
    public const string ChecksBounce = "checks.bounce";
    public const string ChecksCancel = "checks.cancel";
    public const string ChecksExport = "checks.export";

    // ---- Suppliers master (shared by Checks now, Purchases next) ----
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersCreate = "suppliers.create";
    public const string SuppliersEdit = "suppliers.edit";

    // ---- Purchases: requests, orders, receiving, supplier invoices, payments (spec section 35) ----
    public const string PurchasesView = "purchases.view";
    public const string PurchasesCreate = "purchases.create";
    public const string PurchasesEdit = "purchases.edit";
    /// <summary>
    /// Correcting the ORDERED quantity of a purchase-order line that has already been partly
    /// received (spec section 35). Separate from <see cref="PurchasesEdit"/> because it amends a live
    /// order against real receipts - it never rewrites the received quantity or any posted stock
    /// movement, and it can never take the ordered quantity below what has already arrived.
    /// </summary>
    public const string PurchasesEditAfterReceipt = "purchases.edit_after_receipt";
    public const string PurchasesSubmit = "purchases.submit";
    public const string PurchasesApprove = "purchases.approve";
    public const string PurchasesReceive = "purchases.receive";
    public const string PurchasesCancel = "purchases.cancel";
    public const string PurchasesInvoice = "purchases.invoice";
    public const string PurchasesPay = "purchases.pay";
    public const string PurchasesExport = "purchases.export";

    // ---- Payroll & wages: departments, employees, monthly runs (spec section 36) ----
    public const string PayrollView = "payroll.view";
    public const string PayrollManageEmployees = "payroll.employees";
    public const string PayrollCreate = "payroll.create";
    public const string PayrollApprove = "payroll.approve";
    public const string PayrollPost = "payroll.post";
    public const string PayrollCancel = "payroll.cancel";
    /// <summary>Reading and exporting the payroll accounting mappings (spec section 37).</summary>
    public const string PayrollAccounting = "payroll.accounting";

    // ---- HR: sections, attendance, leave, overtime, advances, labor cost (spec sections 28-42) ----
    public const string HrView = "hr.view";
    public const string HrManage = "hr.manage";
    public const string HrAttendance = "hr.attendance";
    public const string HrLeaveRequest = "hr.leave_request";
    public const string HrLeaveApprove = "hr.leave_approve";
    public const string HrOvertimeRequest = "hr.overtime_request";
    public const string HrOvertimeApprove = "hr.overtime_approve";
    public const string HrAdvancesManage = "hr.advances";
    public const string HrLaborCost = "hr.labor_cost";

    // ---- Job Order costing (spec section 34) ----
    public const string CostingView = "costing.view";
    public const string CostingEditEstimate = "costing.edit_estimate";
    /// <summary>Signing off the approved cost of a completed Job Order - a financial decision, kept separate from editing the estimate.</summary>
    public const string CostingApprove = "costing.approve";

    // ---- Customer service price list (spec sections 34 + 36) ----
    // Kept separate from costing: reading the cost list, reading the service price
    // list, and changing either are four different rights. A factory costs its own
    // work; only commercial staff set what a customer is charged.
    public const string PricingView = "pricing.view";
    public const string PricingManage = "pricing.manage";

    // ---- Operating supplies internal issue (spec section 27) ----
    public const string SuppliesView = "supplies.view";
    public const string SuppliesIssue = "supplies.issue";
    public const string SuppliesCancel = "supplies.cancel";

    // ---- Factory-owned materials sales (spec section 26) ----
    public const string MaterialSalesView = "material_sales.view";
    public const string MaterialSalesCreate = "material_sales.create";
    public const string MaterialSalesPost = "material_sales.post";
    public const string MaterialSalesCancel = "material_sales.cancel";

    // ---- Approval Center (spec section 44) ----
    public const string ApprovalsView = "approvals.view";

    // ---- Attachments on business documents (spec section 47) ----
    public const string AttachmentsView = "attachments.view";
    public const string AttachmentsManage = "attachments.manage";

    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";

    /// <summary>Every permission string above, for seeding the default admin account and for any "assign all" UI.</summary>
    public static readonly string[] All =
    {
        Admin,
        CustomersView, CustomersCreate, CustomersEdit,
        ItemsView, ItemsCreate, ItemsEdit,
        RawReceive, RawInspect, RawReceiptCancel, RawConsume, RawReturn, RawReturnCancel, RawExternalRelease, RawTransfer,
        ProductionView, ProductionCreate, ProductionEdit, ProductionExecuteStage, ProductionComplete, ProductionReprocess, ProductionResume, ProductionIssueMaterialCancel, ProductionManageMachines, ProductionManageShifts,
        InventoryView, InventoryEdit, InventoryDelete, InventoryAdjust, InventoryAllowNegativeStock, InventoryApproveNegativeStock,
        ReadyView, ReadyTransfer, ReadyDeliver, ReadyEditPostApproval,
        InvoicesView, InvoicesCreate, InvoicesEdit, InvoicesIssue, InvoicesCancel,
        TreasuryView, TreasuryCreate, TreasuryCancel,
        FormationView, FormationCreate, FormationEdit, FormationSubmit, FormationApprove,
        FormationReject, FormationCancel, FormationDelete, FormationManageGroups,
        FormationManageSpecifications, FormationViewTraceability, FormationPrint, FormationExport,
        ChecksView, ChecksCreate, ChecksEdit, ChecksEndorse, ChecksDeposit, ChecksClear, ChecksBounce, ChecksCancel, ChecksExport,
        SuppliersView, SuppliersCreate, SuppliersEdit,
        PurchasesView, PurchasesCreate, PurchasesEdit, PurchasesEditAfterReceipt, PurchasesSubmit, PurchasesApprove,
        PurchasesReceive, PurchasesCancel, PurchasesInvoice, PurchasesPay, PurchasesExport,
        PayrollView, PayrollManageEmployees, PayrollCreate, PayrollApprove, PayrollPost, PayrollCancel, PayrollAccounting,
        HrView, HrManage, HrAttendance, HrLeaveRequest, HrLeaveApprove, HrOvertimeRequest, HrOvertimeApprove,
        HrAdvancesManage, HrLaborCost,
        CostingView, CostingEditEstimate, CostingApprove,
        PricingView, PricingManage,
        SuppliesView, SuppliesIssue, SuppliesCancel,
        MaterialSalesView, MaterialSalesCreate, MaterialSalesPost, MaterialSalesCancel,
        ApprovalsView,
        AttachmentsView, AttachmentsManage,
        ReportsView, ReportsExport,
        UsersManage, RolesManage, SettingsManage, AuditView
    };
}
