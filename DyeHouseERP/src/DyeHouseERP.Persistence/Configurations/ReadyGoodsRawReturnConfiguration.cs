using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ReadyGoodsRawReturnConfiguration : IEntityTypeConfiguration<ReadyGoodsRawReturn>
{
    public void Configure(EntityTypeBuilder<ReadyGoodsRawReturn> builder)
    {
        builder.ToTable("ReadyGoodsRawReturns");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MovementNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(m => m.MovementNumber).IsUnique();

        builder.Property(m => m.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.QuantityKg).HasPrecision(18, 3);
        builder.Property(m => m.QuantityMeter).HasPrecision(18, 3);
        builder.Property(m => m.Reason).HasMaxLength(500).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(m => m.ProductionOrderId);
        builder.HasIndex(m => m.CustomerId);
        builder.HasIndex(m => m.DestinationRawMessageId);
        builder.HasIndex(m => m.MovementDate);
    }
}
