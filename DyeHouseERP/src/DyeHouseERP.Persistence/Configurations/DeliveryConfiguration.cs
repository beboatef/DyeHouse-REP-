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

        // Sub-customer permit -> primary permit (spec section 31). A real reference, never a
        // cascade: the primary permit is another document and must not be able to delete this one.
        builder.HasIndex(d => d.PrimaryDeliveryId);
        builder.HasOne<Delivery>()
            .WithMany()
            .HasForeignKey(d => d.PrimaryDeliveryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotent delivery confirmation (spec section 31): the token a retry carries, unique only
        // among the rows that actually have one, so every draft and every historical row keeps its NULL.
        builder.HasIndex(d => d.DeliverRequestId)
            .IsUnique()
            .HasFilter("[DeliverRequestId] IS NOT NULL");

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
        builder.Property(l => l.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.RawOrigin).HasMaxLength(500);
        builder.Property(l => l.Notes).HasMaxLength(1000);

        // The original-raw figures a partial delivery settles (spec section 31), stored with the
        // same precision as every other fabric measurement - never converted between units.
        builder.Property(l => l.OriginalRawQuantityKg).HasPrecision(18, 3);
        builder.Property(l => l.OriginalRawQuantityMeter).HasPrecision(18, 3);

        builder.HasIndex(l => l.ProductionOrderId);
    }
}
