using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class PurchaseUnitConfiguration : IEntityTypeConfiguration<PurchaseUnit>
{
    public void Configure(EntityTypeBuilder<PurchaseUnit> builder)
    {
        builder.ToTable("PurchaseUnits");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(u => u.Code).IsUnique();

        builder.Property(u => u.NameAr).HasMaxLength(100).IsRequired();
        builder.Property(u => u.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Notes).HasMaxLength(1000);

        builder.Property(u => u.ConversionFactor).HasPrecision(18, 6);

        builder.Property(u => u.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(u => u.IsActive);

        // A unit that is a conversion target of another unit cannot be deleted out
        // from under it. Deactivation (not deletion) is the supported lifecycle, so
        // Restrict is the correct behaviour rather than a cascade.
        builder.HasOne<PurchaseUnit>()
            .WithMany()
            .HasForeignKey(u => u.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
