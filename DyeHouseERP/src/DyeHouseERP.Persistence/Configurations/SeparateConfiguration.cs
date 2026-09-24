using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class SeparateConfiguration : IEntityTypeConfiguration<Separate>
{
    public void Configure(EntityTypeBuilder<Separate> builder)
    {
        builder.ToTable("Separates");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.QuantityKg).HasPrecision(18, 3);
        builder.Property(s => s.QuantityMeter).HasPrecision(18, 3);
        builder.Property(s => s.Reason).HasMaxLength(500);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => s.OriginalProductionOrderId);
        builder.HasIndex(s => s.StageExecutionId);
        builder.HasIndex(s => s.Status);
    }
}
