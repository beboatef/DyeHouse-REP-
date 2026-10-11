using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

/// <summary>
/// Customer Service Price List (spec section 36). The unique index covers
/// (stage, customer, unit). SQL Server treats NULLs as equal in a unique index, so
/// this also enforces "at most ONE general default per stage+unit" - which is
/// exactly the guarantee the specific-then-general resolver needs.
/// </summary>
public class CustomerServicePriceConfiguration : IEntityTypeConfiguration<CustomerServicePrice>
{
    public void Configure(EntityTypeBuilder<CustomerServicePrice> builder)
    {
        builder.ToTable("CustomerServicePrices");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PricePerUnit).HasPrecision(18, 4);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(p => new { p.StageDefinitionId, p.CustomerId, p.Unit }).IsUnique();
        builder.HasIndex(p => p.CustomerId);
        builder.HasIndex(p => p.IsActive);

        builder.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// The Job Order's own snapshotted price (spec section 34). The unique index is the
/// rule from the confirmed design: ONE snapshot per Job Order per unit. Revenue is
/// therefore computed once for the order against its final actual quantity, never
/// summed per stage.
/// </summary>
public class ProductionOrderServicePriceConfiguration : IEntityTypeConfiguration<ProductionOrderServicePrice>
{
    public void Configure(EntityTypeBuilder<ProductionOrderServicePrice> builder)
    {
        builder.ToTable("ProductionOrderServicePrices");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Unit).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.PricePerUnit).HasPrecision(18, 4);
        builder.Property(s => s.PreviousPricePerUnit).HasPrecision(18, 4);
        builder.Property(s => s.OverrideReason).HasMaxLength(1000);
        builder.Property(s => s.PricedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => new { s.ProductionOrderId, s.Unit }).IsUnique();
        builder.HasIndex(s => s.StageDefinitionId);
    }
}
