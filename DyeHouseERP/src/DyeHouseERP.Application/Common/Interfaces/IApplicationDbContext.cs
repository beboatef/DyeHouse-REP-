using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.Application.Common.Interfaces;

/// <summary>
/// Application-layer view of the DbContext. The Application layer depends
/// only on this interface, never on EF Core's DbContext directly or on the
/// Persistence project - keeping the dependency arrow pointing inward as
/// Clean Architecture requires.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Check> Checks { get; }
    DbSet<CheckMovement> CheckMovements { get; }
    DbSet<FormationRequest> FormationRequests { get; }
    DbSet<FormationGroup> FormationGroups { get; }
    DbSet<FormationBasin> FormationBasins { get; }
    DbSet<FormationSpecTemplate> FormationSpecTemplates { get; }
    DbSet<Item> Items { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<RawMessage> RawMessages { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<DocumentSequenceDefinition> DocumentSequenceDefinitions { get; }
    DbSet<DocumentSequenceCounter> DocumentSequenceCounters { get; }
    DbSet<ProductionStageDefinition> ProductionStageDefinitions { get; }
    DbSet<ProductionOrder> ProductionOrders { get; }
    DbSet<ProductionOrderStageExecution> ProductionOrderStageExecutions { get; }
    DbSet<RawAllocation> RawAllocations { get; }
    DbSet<ReadyGoodsSource> ReadyGoodsSources { get; }
    DbSet<NegativeStockOverride> NegativeStockOverrides { get; }
    DbSet<RawExternalRelease> RawExternalReleases { get; }
    DbSet<CustomerTransfer> CustomerTransfers { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<Separate> Separates { get; }
    DbSet<Material> Materials { get; }
    DbSet<MaterialTransaction> MaterialTransactions { get; }
    DbSet<MaterialTransfer> MaterialTransfers { get; }
    DbSet<MaterialIssue> MaterialIssues { get; }
    DbSet<MaterialPreparation> MaterialPreparations { get; }
    DbSet<ReadyGoodsTransfer> ReadyGoodsTransfers { get; }
    DbSet<Delivery> Deliveries { get; }
    DbSet<DeliveryLine> DeliveryLines { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<CustomerLedgerEntry> CustomerLedgerEntries { get; }
    DbSet<TreasuryAccount> TreasuryAccounts { get; }
    DbSet<TreasuryTransaction> TreasuryTransactions { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<Payment> Payments { get; }
    DbSet<TreasuryTransfer> TreasuryTransfers { get; }
    DbSet<CostEntry> CostEntries { get; }
    DbSet<ProductionRequest> ProductionRequests { get; }
    DbSet<User> Users { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<CompanySettings> CompanySettings { get; }
    DbSet<PeriodClose> PeriodCloses { get; }
    DbSet<SavedReportTemplate> SavedReportTemplates { get; }

    // Purchases (spec section 35)
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<PurchaseReceiptLine> PurchaseReceiptLines { get; }
    DbSet<SupplierInvoice> SupplierInvoices { get; }
    DbSet<SupplierInvoiceLine> SupplierInvoiceLines { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }
    DbSet<SupplierLedgerEntry> SupplierLedgerEntries { get; }

    // Payroll & wages (spec section 36)
    DbSet<Department> Departments { get; }
    DbSet<Employee> Employees { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollRunLine> PayrollRunLines { get; }

    // HR masters, attendance, leave, overtime, advances & wage rules (spec sections 28-42)
    DbSet<HrSection> HrSections { get; }
    DbSet<JobPosition> JobPositions { get; }
    DbSet<CostCenter> CostCenters { get; }
    DbSet<HrShift> HrShifts { get; }
    DbSet<AttendanceRule> AttendanceRules { get; }
    DbSet<AttendanceEvent> AttendanceEvents { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<OvertimeEntry> OvertimeEntries { get; }
    DbSet<EmployeeAdvance> EmployeeAdvances { get; }
    DbSet<PayComponent> PayComponents { get; }
    DbSet<EmployeePayComponent> EmployeePayComponents { get; }
    DbSet<ContributionRule> ContributionRules { get; }
    DbSet<PayrollAccountMapping> PayrollAccountMappings { get; }

    // Approval centre (spec section 24)
    DbSet<ApprovalRequest> ApprovalRequests { get; }

    // Production master data (spec sections 19-20): machines, execution shifts, stage machines
    DbSet<Machine> Machines { get; }
    DbSet<StageMachine> StageMachines { get; }
    DbSet<ProductionShift> ProductionShifts { get; }

    // Operating supplies internal issue (spec section 27)
    DbSet<SupplyIssue> SupplyIssues { get; }
    DbSet<SupplyIssueLine> SupplyIssueLines { get; }

    // Factory-owned materials sales (spec section 26)
    DbSet<MaterialSale> MaterialSales { get; }
    DbSet<MaterialSaleLine> MaterialSaleLines { get; }

    // Purchase units of measure (spec section 44)
    DbSet<PurchaseUnit> PurchaseUnits { get; }

    // Customer returns of processed goods to the raw material warehouse (spec sections 32-33)
    DbSet<CustomerReturn> CustomerReturns { get; }
    DbSet<CustomerReturnLine> CustomerReturnLines { get; }

    // Customer service price list + the per-Job-Order price snapshot (spec sections 34, 36).
    // S8: the fixed per-stage "actual cost list" (StageCostRate) was removed - factory actual
    // cost is always derived from posted transactions, never from a fixed stage rate.
    DbSet<CustomerServicePrice> CustomerServicePrices { get; }
    DbSet<ProductionOrderServicePrice> ProductionOrderServicePrices { get; }

    // Private attachments on business documents (spec section 47)
    DbSet<Attachment> Attachments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
