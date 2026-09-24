using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.SourceDocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(t => t.SourceDocumentNumber).HasMaxLength(30).IsRequired();
        builder.Property(t => t.QuantityKg).HasPrecision(18, 3);
        builder.Property(t => t.QuantityMeter).HasPrecision(18, 3);
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();

        // This table is append-only: rows are never updated after insert.
        // Indexes are chosen for the balance-lookup queries this ledger exists to serve.
        builder.HasIndex(t => new { t.RawMessageId, t.ItemId });
        builder.HasIndex(t => new { t.CustomerId, t.ItemId, t.WarehouseId });
        builder.HasIndex(t => t.ProductionOrderId);
        builder.HasIndex(t => t.TransactionDate);
    }
}
