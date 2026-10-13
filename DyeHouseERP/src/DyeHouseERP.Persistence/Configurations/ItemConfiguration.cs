using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Code).HasMaxLength(30).IsRequired();
        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(i => i.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.BaseUnit).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Structured secondary unit (spec section 5): nullable, and constrained so it
        // can never equal the base unit. The secondary unit is real data, never a note.
        builder.Property(i => i.SecondaryUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.RequireBothUnits).IsRequired().HasDefaultValue(false);

        builder.Ignore(i => i.ConfiguredUnits);

        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(i => i.Code).IsUnique();

        // Data-level guard mirroring the domain rule: a secondary unit is either absent
        // or DIFFERENT from the base unit, and "require both" implies a secondary unit.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Items_SecondaryUnitDiffers",
                "[SecondaryUnit] IS NULL OR [SecondaryUnit] <> [BaseUnit]");
            t.HasCheckConstraint(
                "CK_Items_RequireBothUnitsNeedsSecondary",
                "[RequireBothUnits] = 0 OR [SecondaryUnit] IS NOT NULL");
        });
    }
}
