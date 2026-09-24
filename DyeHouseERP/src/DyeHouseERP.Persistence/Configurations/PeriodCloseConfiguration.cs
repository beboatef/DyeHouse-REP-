using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class PeriodCloseConfiguration : IEntityTypeConfiguration<PeriodClose>
{
    public void Configure(EntityTypeBuilder<PeriodClose> builder)
    {
        builder.ToTable("PeriodCloses");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.ReopenedBy).HasMaxLength(100);
        builder.Property(p => p.ReopenReason).HasMaxLength(1000);
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(p => new { p.PeriodStart, p.PeriodEnd });
    }
}
