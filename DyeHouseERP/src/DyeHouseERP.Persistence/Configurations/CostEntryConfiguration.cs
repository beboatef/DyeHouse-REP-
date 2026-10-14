using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class CostEntryConfiguration : IEntityTypeConfiguration<CostEntry>
{
    public void Configure(EntityTypeBuilder<CostEntry> builder)
    {
        builder.ToTable("CostEntries");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        // Explicitly chargeable to the customer (spec sections 20 + 32). Defaults to false, so every
        // existing cost entry stays company-borne and no invoice can pick it up until somebody
        // deliberately flags it - reprocessing/separate costs are never auto-billed.
        builder.Property(c => c.IsChargeableToCustomer).HasDefaultValue(false);

        builder.HasIndex(c => c.ProductionOrderId);
    }
}
