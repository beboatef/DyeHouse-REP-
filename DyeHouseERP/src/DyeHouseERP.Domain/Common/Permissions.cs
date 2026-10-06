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
    public const string RawConsume = "raw.consume";
    public const string RawReturn = "raw.return";
    public const string RawExternalRelease = "raw.external_release";
    public const string RawTransfer = "raw.transfer";

    public const string ProductionView = "production.view";
    public const string ProductionCreate = "production.create";
    public const string ProductionEdit = "production.edit";
    public const string ProductionExecuteStage = "production.execute_stage";
    public const string ProductionComplete = "production.complete";
    public const string ProductionReprocess = "production.reprocess";
    /// <summary>Gates completing a stage flagged RequiresApproval (spec section 19-20) - distinct from ProductionExecuteStage, which is ordinary stage work.</summary>
    public const string StageRequiresApproval = "production.approve_stage";

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
    public const string InvoicesIssue = "invoices.issue";
    public const string InvoicesCancel = "invoices.cancel";

    public const string TreasuryView = "treasury.view";
    public const string TreasuryCreate = "treasury.create";

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
    public const string FormationConvertToJobOrder = "formation.convert";
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

    // ---- Job Order costing (spec section 34) ----
    public const string CostingView = "costing.view";
    public const string CostingEditEstimate = "costing.edit_estimate";
    /// <summary>Signing off the approved cost of a completed Job Order - a financial decision, kept separate from editing the estimate.</summary>
    public const string CostingApprove = "costing.approve";

    // ---- Commercial price lists (spec sections 34A + 36) ----
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
        RawReceive, RawInspect, RawConsume, RawReturn, RawExternalRelease, RawTransfer,
        ProductionView, ProductionCreate, ProductionEdit, ProductionExecuteStage, ProductionComplete, ProductionReprocess, StageRequiresApproval,
        InventoryView, InventoryEdit, InventoryDelete, InventoryAdjust, InventoryAllowNegativeStock, InventoryApproveNegativeStock,
        ReadyView, ReadyTransfer, ReadyDeliver, ReadyEditPostApproval,
        InvoicesView, InvoicesCreate, InvoicesIssue, InvoicesCancel,
        TreasuryView, TreasuryCreate,
        FormationView, FormationCreate, FormationEdit, FormationSubmit, FormationApprove,
        FormationReject, FormationCancel, FormationConvertToJobOrder, FormationManageGroups,
        FormationManageSpecifications, FormationViewTraceability, FormationPrint, FormationExport,
        ChecksView, ChecksCreate, ChecksEdit, ChecksEndorse, ChecksDeposit, ChecksClear, ChecksBounce, ChecksCancel, ChecksExport,
        SuppliersView, SuppliersCreate, SuppliersEdit,
        PurchasesView, PurchasesCreate, PurchasesEdit, PurchasesSubmit, PurchasesApprove,
        PurchasesReceive, PurchasesCancel, PurchasesInvoice, PurchasesPay, PurchasesExport,
        PayrollView, PayrollManageEmployees, PayrollCreate, PayrollApprove, PayrollPost, PayrollCancel,
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
