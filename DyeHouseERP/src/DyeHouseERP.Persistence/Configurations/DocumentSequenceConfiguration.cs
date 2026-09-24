using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class DocumentSequenceDefinitionConfiguration : IEntityTypeConfiguration<DocumentSequenceDefinition>
{
    public void Configure(EntityTypeBuilder<DocumentSequenceDefinition> builder)
    {
        builder.ToTable("DocumentSequenceDefinitions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(d => d.Prefix).HasMaxLength(10).IsRequired();

        builder.HasIndex(d => d.DocumentType).IsUnique();
    }
}

public class DocumentSequenceCounterConfiguration : IEntityTypeConfiguration<DocumentSequenceCounter>
{
    public void Configure(EntityTypeBuilder<DocumentSequenceCounter> builder)
    {
        builder.ToTable("DocumentSequenceCounters");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DocumentType).HasConversion<string>().HasMaxLength(40).IsRequired();

        // Exactly one counter row per (DocumentType, Year, WarehouseId) combination.
        // SQL Server treats NULLs as distinct in unique indexes, which is what we
        // want here (non-yearly / non-warehouse-scoped sequences use NULL).
        builder.HasIndex(c => new { c.DocumentType, c.Year, c.WarehouseId }).IsUnique();
    }
}
