using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

/// <summary>
/// Explicit item-scoped unit conversion rules (spec sections 5-6). The unique index on
/// (ItemId, Operation) is what makes a lookup unambiguous: at most ONE rule per item and
/// operation, so a caller can never have to pick between two different factors.
/// </summary>
public class ItemUnitConversionConfiguration : IEntityTypeConfiguration<ItemUnitConversion>
{
    public void Configure(EntityTypeBuilder<ItemUnitConversion> builder)
    {
        builder.ToTable("ItemUnitConversions");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FromUnit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.ToUnit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Factor).HasPrecision(18, 6);
        builder.Property(c => c.Operation).HasMaxLength(60).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(500);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(c => new { c.ItemId, c.Operation }).IsUnique();
        builder.HasIndex(c => c.ItemId);

        builder.HasOne<Item>().WithMany().HasForeignKey(c => c.ItemId).OnDelete(DeleteBehavior.Cascade);

        // Data-level guard mirroring the domain rule: a rule always converts between two
        // DIFFERENT units and always uses a positive factor.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ItemUnitConversions_DifferentUnits", "[FromUnit] <> [ToUnit]");
            t.HasCheckConstraint("CK_ItemUnitConversions_PositiveFactor", "[Factor] > 0");
        });
    }
}
