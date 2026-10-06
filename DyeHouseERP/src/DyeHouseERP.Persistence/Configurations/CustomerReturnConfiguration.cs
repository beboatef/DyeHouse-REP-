using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class CustomerReturnConfiguration : IEntityTypeConfiguration<CustomerReturn>
{
    public void Configure(EntityTypeBuilder<CustomerReturn> builder)
    {
        builder.ToTable("CustomerReturns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReturnNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.ReturnNumber).IsUnique();

        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.Notes).HasMaxLength(2000);

        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.Metadata.FindNavigation(nameof(CustomerReturn.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.CustomerReturnId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.ReturnDate);
    }
}

public class CustomerReturnLineConfiguration : IEntityTypeConfiguration<CustomerReturnLine>
{
    public void Configure(EntityTypeBuilder<CustomerReturnLine> builder)
    {
        builder.ToTable("CustomerReturnLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.QuantityKg).HasPrecision(18, 3);
        builder.Property(l => l.QuantityMeter).HasPrecision(18, 3);
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.HasIndex(l => l.CustomerReturnId);

        // The lot a return lands in, and the Job Order/basin it came from, are
        // both looked up by traceability reports, so they are indexed.
        builder.HasIndex(l => l.RawMessageId);
        builder.HasIndex(l => l.ProductionOrderId);
        builder.HasIndex(l => l.FormationGroupId);
    }
}
