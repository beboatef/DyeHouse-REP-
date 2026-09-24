using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("CompanySettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CompanyNameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CompanyNameEn).HasMaxLength(200).IsRequired();
        // A logo image as base64 can be a few hundred KB - nvarchar(max) avoids truncation.
        builder.Property(s => s.LogoDataUrl).HasColumnType("nvarchar(max)");
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);
    }
}
