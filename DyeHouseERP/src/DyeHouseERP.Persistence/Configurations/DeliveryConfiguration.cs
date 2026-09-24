using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DeliveryNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(d => d.DeliveryNumber).IsUnique();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Notes).HasMaxLength(2000);
        builder.Property(d => d.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(d => d.ModifiedBy).HasMaxLength(100);

        builder.Metadata.FindNavigation(nameof(Delivery.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DeliveryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.CustomerId);
        builder.HasIndex(d => d.Status);
    }
}

public class DeliveryLineConfiguration : IEntityTypeConfiguration<DeliveryLine>
{
    public void Configure(EntityTypeBuilder<DeliveryLine> builder)
    {
        builder.ToTable("DeliveryLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Color).HasMaxLength(100);
        builder.Property(l => l.QuantityKg).HasPrecision(18, 3);
        builder.Property(l => l.QuantityMeter).HasPrecision(18, 3);
        builder.Property(l => l.RawOrigin).HasMaxLength(500);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.HasIndex(l => l.ProductionOrderId);
    }
}
