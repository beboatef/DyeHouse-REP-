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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
