using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistory>
{
    public void Configure(EntityTypeBuilder<PasswordHistory> builder)
    {
        builder.ToTable("PasswordHistories");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(h => h.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(h => h.ModifiedBy).HasMaxLength(100);
        // Newest-first lookup per user when a password change checks the last entries.
        builder.HasIndex(h => new { h.UserId, h.CreatedAtUtc });
        builder.HasOne<User>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
