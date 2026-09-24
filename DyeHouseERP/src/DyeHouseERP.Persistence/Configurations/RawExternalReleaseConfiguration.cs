using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class RawExternalReleaseConfiguration : IEntityTypeConfiguration<RawExternalRelease>
{
    public void Configure(EntityTypeBuilder<RawExternalRelease> builder)
    {
        builder.ToTable("RawExternalReleases");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReleaseNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.ReleaseNumber).IsUnique();

        builder.Property(r => r.QuantityKg).HasPrecision(18, 3);
        builder.Property(r => r.QuantityMeter).HasPrecision(18, 3);
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.ExternalParty).HasMaxLength(200);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(r => r.RawMessageId);
        builder.HasIndex(r => r.CustomerId);
    }
}

public class CustomerTransferConfiguration : IEntityTypeConfiguration<CustomerTransfer>
{
    public void Configure(EntityTypeBuilder<CustomerTransfer> builder)
    {
        builder.ToTable("CustomerTransfers");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TransferNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.TransferNumber).IsUnique();

        builder.Property(t => t.QuantityKg).HasPrecision(18, 3);
        builder.Property(t => t.QuantityMeter).HasPrecision(18, 3);
        builder.Property(t => t.Reason).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.ApprovedBy).HasMaxLength(100);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(t => t.RawMessageId);
        builder.HasIndex(t => t.FromCustomerId);
        builder.HasIndex(t => t.ToCustomerId);

        // A transfer's two customers are conceptually two FKs into the same
        // Customers table - EF needs Restrict (not the default Cascade) on
        // at least one side to avoid SQL Server's multiple-cascade-path error.
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.FromCustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.ToCustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
