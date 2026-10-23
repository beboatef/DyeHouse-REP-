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
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(20);

        // Prevent duplicate transfer for the same order (spec section 29).
        builder.HasIndex(t => new { t.ProductionOrderId, t.Category }).IsUnique().HasFilter("[Status] = N'Posted'");

        // Traceability (spec section 23): the stage whose output this transfer moved.
        // Mapped as a real reference (never a cascade) so the chain
        // order -> stage execution -> ready goods lot cannot be broken from the database side.
        builder.HasIndex(t => t.StageExecutionId);

        builder.HasOne<ProductionOrderStageExecution>()
            .WithMany()
            .HasForeignKey(t => t.StageExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
