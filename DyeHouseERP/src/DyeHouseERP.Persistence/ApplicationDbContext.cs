using System.Data;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    public DbSet<ItemUnitConversion> ItemUnitConversions => Set<ItemUnitConversion>();
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
    public DbSet<ReadyGoodsRawReturn> ReadyGoodsRawReturns => Set<ReadyGoodsRawReturn>();
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

    // HR masters, attendance, leave, overtime, advances & wage rules (spec sections 28-42)
    public DbSet<HrSection> HrSections => Set<HrSection>();
    public DbSet<JobPosition> JobPositions => Set<JobPosition>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<HrShift> HrShifts => Set<HrShift>();
    public DbSet<AttendanceRule> AttendanceRules => Set<AttendanceRule>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<OvertimeEntry> OvertimeEntries => Set<OvertimeEntry>();
    public DbSet<EmployeeAdvance> EmployeeAdvances => Set<EmployeeAdvance>();
    public DbSet<PayComponent> PayComponents => Set<PayComponent>();
    public DbSet<EmployeePayComponent> EmployeePayComponents => Set<EmployeePayComponent>();
    public DbSet<ContributionRule> ContributionRules => Set<ContributionRule>();
    public DbSet<PayrollAccountMapping> PayrollAccountMappings => Set<PayrollAccountMapping>();

    // Approval centre (spec section 24)
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    // Production master data (spec sections 19-20): machines, execution shifts, stage machines
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<StageMachine> StageMachines => Set<StageMachine>();
    public DbSet<ProductionShift> ProductionShifts => Set<ProductionShift>();

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

    // Customer service price list + the per-Job-Order price snapshot (spec sections 34, 36).
    // S8: the fixed per-stage "actual cost list" (StageCostRate) was removed - factory actual
    // cost is always derived from posted transactions, never from a fixed stage rate.
    public DbSet<CustomerServicePrice> CustomerServicePrices => Set<CustomerServicePrice>();
    public DbSet<ProductionOrderServicePrice> ProductionOrderServicePrices => Set<ProductionOrderServicePrice>();

    // Private attachments on business documents (spec section 47)
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// H7 - write-time warehouse guard (the check-then-act window, closed once for every
    /// stock path instead of re-ordered in each handler).
    ///
    /// Every handler checks that the warehouse it is about to post into exists and is still
    /// active (see WarehouseGuard). That check and the INSERT are nevertheless two separate
    /// statements, and deactivating a warehouse takes no lock that any stock path holds: an
    /// administrator deactivating the warehouse in between would let a brand new stock row
    /// land in a retired warehouse.
    ///
    /// On SQL Server the check is therefore repeated HERE, inside the same transaction as the
    /// INSERT and as a row lock on the warehouse row (`WITH (UPDLOCK, HOLDLOCK)`). A
    /// concurrent deactivation has to queue behind this transaction (and vice versa), so the
    /// two operations are serialized and whichever runs second sees the committed state. The
    /// business rule is unchanged - a deactivated warehouse never receives a new ledger row,
    /// and the escape hatch stays the same (reactivate, then post) - only the check moved to
    /// the point where the row is actually written.
    ///
    /// Deliberately SQL Server-only: the row lock is T-SQL with no InMemory/SQLite
    /// equivalent, so other providers keep the handler-level guard alone. The transaction is
    /// started through the context's own execution strategy because
    /// SqlServerRetryingExecutionStrategy refuses a user-initiated transaction, and the row
    /// locks must live in the same transaction as the INSERT to be worth anything.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var stockWarehouseIds = NewStockMovementWarehouseIds();

        if (stockWarehouseIds.Count == 0 || !Database.IsSqlServer())
            return await base.SaveChangesAsync(cancellationToken);

        if (Database.CurrentTransaction is not null)
        {
            foreach (var warehouseId in stockWarehouseIds.OrderBy(id => id))
                await EnsureWarehouseActiveUnderRowLockAsync(warehouseId, cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

            // Fixed (ascending id) acquisition order, so two concurrent postings that touch
            // the same set of warehouses can never take these row locks in opposite orders.
            foreach (var warehouseId in stockWarehouseIds.OrderBy(id => id))
                await EnsureWarehouseActiveUnderRowLockAsync(warehouseId, cancellationToken);

            // Do not accept tracked changes before the transaction has committed: a
            // rollback must leave the entities pending, not silently lose the ledger rows.
            var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ChangeTracker.AcceptAllChanges();
            return written;
        });
    }

    /// <summary>
    /// The warehouses of every stock-ledger row this SaveChanges is about to INSERT. Only
    /// ADDED rows count: re-reading or updating existing history is never a new movement.
    /// </summary>
    private List<Guid> NewStockMovementWarehouseIds()
    {
        var ids = new HashSet<Guid>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;

            switch (entry.Entity)
            {
                case InventoryTransaction inventory:
                    ids.Add(inventory.WarehouseId);
                    break;
                case MaterialTransaction material:
                    ids.Add(material.WarehouseId);
                    break;
            }
        }

        return ids.ToList();
    }

    private async Task EnsureWarehouseActiveUnderRowLockAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var connection = Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            "SELECT [IsActive], [Code], [Name] FROM [Warehouses] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @warehouseId";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@warehouseId";
        parameter.Value = warehouseId;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new NotFoundException("Warehouse", warehouseId);

        if (!Convert.ToBoolean(reader.GetValue(0)))
        {
            var code = reader.GetValue(1)?.ToString() ?? string.Empty;
            var name = reader.GetValue(2)?.ToString() ?? string.Empty;

            throw new DomainException(
                $"Warehouse '{name}' ({code}) was deactivated while this stock movement was being posted, so the movement was refused " +
                "and nothing was written. Existing movements and documents remain readable and unchanged; reactivate the warehouse to post into it again.");
        }
    }
}
