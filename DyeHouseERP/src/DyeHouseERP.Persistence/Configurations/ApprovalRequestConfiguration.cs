using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.DocumentNumber).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.PayloadJson).IsRequired();
        builder.Property(a => a.RequestedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.DecidedBy).HasMaxLength(100);
        builder.Property(a => a.DecisionReason).HasMaxLength(1000);
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        // Stored as text so a readable queue/report never shows magic numbers.
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // The approval inbox reads pending work by state, newest decisions by
        // subject; both are hot paths on a screen that polls.
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => new { a.Type, a.DocumentId, a.Status });
    }
}
