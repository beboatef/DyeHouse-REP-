using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Persistence;

/// <summary>
/// The single EF Core DbContext for the system. Deliberately NOT split into
/// multiple contexts - this is a transactional ERP where cross-module
/// consistency (e.g. a raw receipt AND its ledger row committing together)
/// matters more than modular DbContext purity.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<CheckMovement> CheckMovements => Set<CheckMovement>();
    public DbSet<FormationRequest> FormationRequests => Set<FormationRequest>();
    public DbSet<FormationGroup> FormationGroups => Set<FormationGroup>();
    public DbSet<FormationBasin> FormationBasins => Set<FormationBasin>();
    public DbSet<FormationSpecTemplate> FormationSpecTemplates => Set<FormationSpecTemplate>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<RawMessage> RawMessages => Set<RawMessage>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<DocumentSequenceDefinition> DocumentSequenceDefinitions => Set<DocumentSequenceDefinition>();
    public DbSet<DocumentSequenceCounter> DocumentSequenceCounters => Set<DocumentSequenceCounter>();
    public DbSet<ProductionStageDefinition> ProductionStageDefinitions => Set<ProductionStageDefinition>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderStageExecution> ProductionOrderStageExecutions => Set<ProductionOrderStageExecution>();
    public DbSet<RawAllocation> RawAllocations => Set<RawAllocation>();
    public DbSet<NegativeStockOverride> NegativeStockOverrides => Set<NegativeStockOverride>();
    public DbSet<RawExternalRelease> RawExternalReleases => Set<RawExternalRelease>();
    public DbSet<CustomerTransfer> CustomerTransfers => Set<CustomerTransfer>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<Separate> Separates => Set<Separate>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialTransaction> MaterialTransactions => Set<MaterialTransaction>();
    public DbSet<MaterialTransfer> MaterialTransfers => Set<MaterialTransfer>();
    public DbSet<MaterialIssue> MaterialIssues => Set<MaterialIssue>();
    public DbSet<MaterialPreparation> MaterialPreparations => Set<MaterialPreparation>();
    public DbSet<ReadyGoodsTransfer> ReadyGoodsTransfers => Set<ReadyGoodsTransfer>();
    public DbSet<ReadyGoodsSource> ReadyGoodsSources => Set<ReadyGoodsSource>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
    public DbSet<TreasuryAccount> TreasuryAccounts => Set<TreasuryAccount>();
    public DbSet<TreasuryTransaction> TreasuryTransactions => Set<TreasuryTransaction>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<TreasuryTransfer> TreasuryTransfers => Set<TreasuryTransfer>();
    public DbSet<CostEntry> CostEntries => Set<CostEntry>();
    public DbSet<ProductionRequest> ProductionRequests => Set<ProductionRequest>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<PeriodClose> PeriodCloses => Set<PeriodClose>();
    public DbSet<SavedReportTemplate> SavedReportTemplates => Set<SavedReportTemplate>();

    // Purchases (spec section 35)
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptLine> PurchaseReceiptLines => Set<PurchaseReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();

    // Payroll & wages (spec section 36)
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollRunLine> PayrollRunLines => Set<PayrollRunLine>();

    // Operating supplies internal issue (spec section 27)
    public DbSet<SupplyIssue> SupplyIssues => Set<SupplyIssue>();
    public DbSet<SupplyIssueLine> SupplyIssueLines => Set<SupplyIssueLine>();

    // Factory-owned materials sales (spec section 26)
    public DbSet<MaterialSale> MaterialSales => Set<MaterialSale>();
    public DbSet<MaterialSaleLine> MaterialSaleLines => Set<MaterialSaleLine>();

    // Purchase units of measure (spec section 44)
    public DbSet<PurchaseUnit> PurchaseUnits => Set<PurchaseUnit>();

    // Customer returns of processed goods to the raw material warehouse (spec sections 32-33)
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<CustomerReturnLine> CustomerReturnLines => Set<CustomerReturnLine>();

    // Commercial price lists + the per-Job-Order price snapshot (spec sections 34A, 34, 36)
    public DbSet<StageCostRate> StageCostRates => Set<StageCostRate>();
    public DbSet<CustomerServicePrice> CustomerServicePrices => Set<CustomerServicePrice>();
    public DbSet<ProductionOrderServicePrice> ProductionOrderServicePrices => Set<ProductionOrderServicePrice>();

    // Private attachments on business documents (spec section 47)
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
