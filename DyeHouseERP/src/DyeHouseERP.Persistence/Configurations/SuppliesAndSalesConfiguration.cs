using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

/// <summary>
/// Operating supplies internal issue (spec section 27). No production order
/// dimension exists here on purpose - supplies are internal consumption and
/// are never charged to a Job Order.
/// </summary>
public class SupplyIssueConfiguration : IEntityTypeConfiguration<SupplyIssue>
{
    public void Configure(EntityTypeBuilder<SupplyIssue> builder)
    {
        builder.ToTable("SupplyIssues");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.IssueNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(i => i.IssueNumber).IsUnique();

        builder.Property(i => i.IssuedTo).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.PostedBy).HasMaxLength(100);
        builder.Property(i => i.CancelledBy).HasMaxLength(100);
        builder.Property(i => i.CancellationReason).HasMaxLength(1000);
        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(100);

        // Derived members are never columns that could drift from the lines.
        builder.Ignore(i => i.TotalCost);
        builder.Ignore(i => i.IsEditable);

        builder.HasIndex(i => i.WarehouseId);
        builder.HasIndex(i => i.DepartmentId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.IssueDate);

        builder.Metadata.FindNavigation(nameof(SupplyIssue.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.SupplyIssueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupplyIssueLineConfiguration : IEntityTypeConfiguration<SupplyIssueLine>
{
    public void Configure(EntityTypeBuilder<SupplyIssueLine> builder)
    {
        builder.ToTable("SupplyIssueLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(1000);

        builder.Ignore(l => l.TotalCost);

        builder.HasIndex(l => l.MaterialId);
        builder.HasIndex(l => l.SupplyIssueId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_SupplyIssueLines_QuantityPositive", "[Quantity] > 0 AND [UnitCost] >= 0"));
    }
}

/// <summary>Sale of factory-owned materials/chemicals (spec section 26).</summary>
public class MaterialSaleConfiguration : IEntityTypeConfiguration<MaterialSale>
{
    public void Configure(EntityTypeBuilder<MaterialSale> builder)
    {
        builder.ToTable("MaterialSales");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SaleNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(s => s.SaleNumber).IsUnique();

        builder.Property(s => s.BuyerName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.PaymentMethod).HasMaxLength(100);
        builder.Property(s => s.Discount).HasPrecision(18, 2);
        builder.Property(s => s.Tax).HasPrecision(18, 2);
        builder.Property(s => s.Notes).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.PostedBy).HasMaxLength(100);
        builder.Property(s => s.CancelledBy).HasMaxLength(100);
        builder.Property(s => s.CancellationReason).HasMaxLength(1000);
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.Ignore(s => s.SubTotal);
        builder.Ignore(s => s.Total);
        builder.Ignore(s => s.IsEditable);

        builder.HasIndex(s => s.WarehouseId);
        builder.HasIndex(s => s.CustomerId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.SaleDate);

        builder.Metadata.FindNavigation(nameof(MaterialSale.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey(l => l.MaterialSaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MaterialSaleLineConfiguration : IEntityTypeConfiguration<MaterialSaleLine>
{
    public void Configure(EntityTypeBuilder<MaterialSaleLine> builder)
    {
        builder.ToTable("MaterialSaleLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasPrecision(18, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(500);

        builder.Ignore(l => l.LineTotal);

        builder.HasIndex(l => l.MaterialId);
        builder.HasIndex(l => l.MaterialSaleId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_MaterialSaleLines_QuantityPositive", "[Quantity] > 0 AND [UnitPrice] >= 0"));
    }
}

/// <summary>Private attachments on any business document (spec section 47).</summary>
public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(400).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.UploadedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Content).IsRequired();

        // The one lookup that always happens: "attachments of this record".
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.UploadedAtUtc);
    }
}
