using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ProductionRequestConfiguration : IEntityTypeConfiguration<ProductionRequest>
{
    public void Configure(EntityTypeBuilder<ProductionRequest> builder)
    {
        builder.ToTable("ProductionRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RequestNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.RequestNumber).IsUnique();
        builder.Property(r => r.Color).HasMaxLength(100);
        builder.Property(r => r.RequestedQuantityKg).HasPrecision(18, 3);
        builder.Property(r => r.RequestedQuantityMeter).HasPrecision(18, 3);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.StaffNotes).HasMaxLength(1000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.Status);
    }
}
