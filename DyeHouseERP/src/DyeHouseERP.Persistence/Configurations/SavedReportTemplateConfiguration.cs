using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class SavedReportTemplateConfiguration : IEntityTypeConfiguration<SavedReportTemplate>
{
    public void Configure(EntityTypeBuilder<SavedReportTemplate> builder)
    {
        builder.ToTable("SavedReportTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(t => t.EntityKey).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ColumnsCsv).HasMaxLength(1000).IsRequired();
        builder.Property(t => t.FiltersJson).HasColumnType("nvarchar(max)");
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
    }
}
