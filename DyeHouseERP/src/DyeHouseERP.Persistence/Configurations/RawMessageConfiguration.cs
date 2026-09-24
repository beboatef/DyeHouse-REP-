using Microsoft.EntityFrameworkCore.Metadata;
using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class RawMessageConfiguration : IEntityTypeConfiguration<RawMessage>
{
    public void Configure(EntityTypeBuilder<RawMessage> builder)
    {
        builder.ToTable("RawMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MessageNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(m => m.MessageNumber).IsUnique();

        builder.Property(m => m.ReceivingUser).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.Property(m => m.Inspector).HasMaxLength(100);
        builder.Property(m => m.InspectionNotes).HasMaxLength(1000);
        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(100);

        builder.Property(m => m.InspectionStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(m => m.CustomerId);
        builder.HasIndex(m => m.WarehouseId);

        builder.Metadata.FindNavigation(nameof(RawMessage.Lines))!
            .SetPropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);

        builder.HasMany(m => m.Lines)
            .WithOne()
            .HasForeignKey(l => l.RawMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RawMessageLineConfiguration : IEntityTypeConfiguration<RawMessageLine>
{
    public void Configure(EntityTypeBuilder<RawMessageLine> builder)
    {
        builder.ToTable("RawMessageLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.QuantityKg).HasPrecision(18, 3);
        builder.Property(l => l.QuantityMeter).HasPrecision(18, 3);
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.HasIndex(l => l.ItemId);

        // Data-level guard mirroring the domain rule: at least one of the
        // two independent quantities must be present (spec section 5).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RawMessageLines_AtLeastOneQuantity",
            "[QuantityKg] IS NOT NULL OR [QuantityMeter] IS NOT NULL"));
    }
}
