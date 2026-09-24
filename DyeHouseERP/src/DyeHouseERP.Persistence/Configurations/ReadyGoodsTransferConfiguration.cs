using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ReadyGoodsTransferConfiguration : IEntityTypeConfiguration<ReadyGoodsTransfer>
{
    public void Configure(EntityTypeBuilder<ReadyGoodsTransfer> builder)
    {
        builder.ToTable("ReadyGoodsTransfers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TransferNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.TransferNumber).IsUnique();
        builder.Property(t => t.QuantityKg).HasPrecision(18, 3);
        builder.Property(t => t.QuantityMeter).HasPrecision(18, 3);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        // Prevent duplicate transfer for the same order (spec section 29).
        builder.HasIndex(t => t.ProductionOrderId).IsUnique();
    }
}
