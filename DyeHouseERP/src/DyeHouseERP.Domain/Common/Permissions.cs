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

    public const string InvoicesView = "invoices.view";
    public const string InvoicesCreate = "invoices.create";
    public const string InvoicesIssue = "invoices.issue";
    public const string InvoicesCancel = "invoices.cancel";

    public const string TreasuryView = "treasury.view";
    public const string TreasuryCreate = "treasury.create";

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
        ReadyView, ReadyTransfer, ReadyDeliver,
        InvoicesView, InvoicesCreate, InvoicesIssue, InvoicesCancel,
        TreasuryView, TreasuryCreate,
        ReportsView, ReportsExport,
        UsersManage, RolesManage, SettingsManage, AuditView
    };
}
