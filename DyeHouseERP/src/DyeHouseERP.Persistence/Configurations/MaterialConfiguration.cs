using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Code).HasMaxLength(30).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Unit).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.PurchasePrice).HasPrecision(18, 4);
        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(m => m.Code).IsUnique();
    }
}

public class MaterialTransactionConfiguration : IEntityTypeConfiguration<MaterialTransaction>
{
    public void Configure(EntityTypeBuilder<MaterialTransaction> builder)
    {
        builder.ToTable("MaterialTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.SourceDocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(t => t.SourceDocumentNumber).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Quantity).HasPrecision(18, 3);
        builder.Property(t => t.UnitCost).HasPrecision(18, 4);
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => new { t.MaterialId, t.WarehouseId });
        builder.HasIndex(t => t.ProductionOrderId);
    }
}

public class MaterialTransferConfiguration : IEntityTypeConfiguration<MaterialTransfer>
{
    public void Configure(EntityTypeBuilder<MaterialTransfer> builder)
    {
        builder.ToTable("MaterialTransfers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TransferNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.TransferNumber).IsUnique();
        builder.Property(t => t.Quantity).HasPrecision(18, 3);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
    }
}

public class MaterialIssueConfiguration : IEntityTypeConfiguration<MaterialIssue>
{
    public void Configure(EntityTypeBuilder<MaterialIssue> builder)
    {
        builder.ToTable("MaterialIssues");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.IssueNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(i => i.IssueNumber).IsUnique();
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ModifiedBy).HasMaxLength(100);
        builder.Ignore(i => i.TotalCost);
        builder.HasIndex(i => i.ProductionOrderId);
    }
}

public class MaterialPreparationConfiguration : IEntityTypeConfiguration<MaterialPreparation>
{
    public void Configure(EntityTypeBuilder<MaterialPreparation> builder)
    {
        builder.ToTable("MaterialPreparations");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PreparationNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.PreparationNumber).IsUnique();
        builder.Property(p => p.OriginalQuantity).HasPrecision(18, 3);
        builder.Property(p => p.WaterQuantity).HasPrecision(18, 3);
        builder.Property(p => p.ResultingQuantity).HasPrecision(18, 3);
        builder.Property(p => p.Concentration).HasPrecision(9, 4);
        builder.Property(p => p.Cost).HasPrecision(18, 4);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(p => p.ProductionOrderId);
    }
}
