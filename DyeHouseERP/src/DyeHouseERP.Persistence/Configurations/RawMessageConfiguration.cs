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

        // Derived/behavioural members - never persisted columns.
        builder.Ignore(m => m.IsAvailableForAllocation);
        builder.Ignore(m => m.HasRejections);

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
        builder.Property(l => l.RejectedQuantityKg).HasPrecision(18, 3);
        builder.Property(l => l.RejectedQuantityMeter).HasPrecision(18, 3);
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.AcceptedQuantityKg);
        builder.Ignore(l => l.AcceptedQuantityMeter);

        builder.HasIndex(l => l.ItemId);

        // Data-level guard mirroring the domain rule: at least one of the
        // two independent quantities must be present (spec section 5).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RawMessageLines_AtLeastOneQuantity",
            "[QuantityKg] IS NOT NULL OR [QuantityMeter] IS NOT NULL"));

        // Data-level guard mirroring the domain rule on rejections: a rejected
        // quantity can never exceed what was actually received (spec section 55),
        // and can never be negative.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RawMessageLines_RejectedWithinReceived",
            "([RejectedQuantityKg] IS NULL OR ([RejectedQuantityKg] >= 0 AND [QuantityKg] IS NOT NULL AND [RejectedQuantityKg] <= [QuantityKg]))" +
            " AND ([RejectedQuantityMeter] IS NULL OR ([RejectedQuantityMeter] >= 0 AND [QuantityMeter] IS NOT NULL AND [RejectedQuantityMeter] <= [QuantityMeter]))"));
    }
}
