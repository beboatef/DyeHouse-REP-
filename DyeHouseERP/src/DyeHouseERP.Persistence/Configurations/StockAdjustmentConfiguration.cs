using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("StockAdjustments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AdjustmentNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(a => a.AdjustmentNumber).IsUnique();

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.ApprovedBy).HasMaxLength(100);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        foreach (var qty in new[]
        {
            nameof(StockAdjustment.QuantityBeforeKg), nameof(StockAdjustment.QuantityBeforeMeter),
            nameof(StockAdjustment.AdjustmentQuantityKg), nameof(StockAdjustment.AdjustmentQuantityMeter),
            nameof(StockAdjustment.QuantityAfterKg), nameof(StockAdjustment.QuantityAfterMeter)
        })
        {
            builder.Property(qty).HasPrecision(18, 3);
        }

        builder.HasIndex(a => a.RawMessageId);
        builder.HasIndex(a => a.CustomerId);
    }
}
