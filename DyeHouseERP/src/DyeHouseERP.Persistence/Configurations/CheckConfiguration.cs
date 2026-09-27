using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class CheckConfiguration : IEntityTypeConfiguration<Check>
{
    public void Configure(EntityTypeBuilder<Check> builder)
    {
        builder.ToTable("Checks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CheckNumber).HasMaxLength(50).IsRequired();
        builder.Property(c => c.BankName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.BranchName).HasMaxLength(200);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.Currency).HasMaxLength(10).IsRequired();
        builder.Property(c => c.Issuer).HasMaxLength(200).IsRequired();
        builder.Property(c => c.OriginalHolder).HasMaxLength(200).IsRequired();
        builder.Property(c => c.CurrentHolder).HasMaxLength(200).IsRequired();
        builder.Property(c => c.CustomerReference).HasMaxLength(200);
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.BounceReason).HasMaxLength(1000);
        builder.Property(c => c.CancellationReason).HasMaxLength(1000);
        builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.Property(c => c.Direction).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.CurrentHolderType).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Not unique on CheckNumber alone: the same serial can legitimately
        // exist at two banks. Duplicate detection (spec section 55) is done per
        // (bank, number, customer) in the command handler with a clear message.
        builder.HasIndex(c => new { c.BankName, c.CheckNumber });
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.Direction);
        builder.HasIndex(c => c.DueDate);
        builder.HasIndex(c => c.CustomerId);
        builder.HasIndex(c => c.SupplierId);

        builder.Metadata.FindNavigation(nameof(Check.Movements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(c => c.Movements)
            .WithOne()
            .HasForeignKey(m => m.CheckId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CheckMovementConfiguration : IEntityTypeConfiguration<CheckMovement>
{
    public void Configure(EntityTypeBuilder<CheckMovement> builder)
    {
        builder.ToTable("CheckMovements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.ToHolderType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.FromHolder).HasMaxLength(200).IsRequired();
        builder.Property(m => m.ToHolder).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(1000);
        builder.Property(m => m.Notes).HasMaxLength(2000);
        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(m => new { m.CheckId, m.MovementDate });
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(s => s.AccountNumber).HasMaxLength(30).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.ContactPerson).HasMaxLength(200);
        builder.Property(s => s.TaxNumber).HasMaxLength(50);
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => s.AccountNumber);
    }
}
