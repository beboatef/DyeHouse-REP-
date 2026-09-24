using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ProductionStageDefinitionConfiguration : IEntityTypeConfiguration<ProductionStageDefinition>
{
    public void Configure(EntityTypeBuilder<ProductionStageDefinition> builder)
    {
        builder.ToTable("ProductionStageDefinitions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.Sequence);
    }
}
