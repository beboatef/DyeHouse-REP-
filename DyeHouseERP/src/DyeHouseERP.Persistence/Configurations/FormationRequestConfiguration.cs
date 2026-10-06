using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class FormationRequestConfiguration : IEntityTypeConfiguration<FormationRequest>
{
    public void Configure(EntityTypeBuilder<FormationRequest> builder)
    {
        builder.ToTable("FormationRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(r => r.RequestNumber).IsUnique();

        builder.Property(r => r.TotalQuantity).HasPrecision(18, 3);
        builder.Property(r => r.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.SubmittedBy).HasMaxLength(100);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.RejectedBy).HasMaxLength(100);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        builder.Property(r => r.CancelledBy).HasMaxLength(100);
        builder.Property(r => r.CancellationReason).HasMaxLength(1000);
        builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(r => r.CustomerId);
        builder.HasIndex(r => r.ItemId);
        builder.HasIndex(r => r.RawMessageId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.ProductionOrderId);
        builder.HasIndex(r => r.RequestDate);

        // The groups are part of the aggregate: loaded with the request,
        // written with the request, never edited from the outside.
        builder.Metadata.FindNavigation(nameof(FormationRequest.Groups))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Groups)
            .WithOne()
            .HasForeignKey(g => g.FormationRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FormationGroupConfiguration : IEntityTypeConfiguration<FormationGroup>
{
    public void Configure(EntityTypeBuilder<FormationGroup> builder)
    {
        builder.ToTable("FormationGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(200);
        builder.Property(g => g.PlannedQuantity).HasPrecision(18, 3);
        builder.Property(g => g.ProducedQuantity).HasPrecision(18, 3);
        builder.Property(g => g.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(g => g.Color).HasMaxLength(100);
        builder.Property(g => g.WidthCm).HasPrecision(18, 3);
        builder.Property(g => g.MetersPerKg).HasPrecision(18, 4);
        builder.Property(g => g.Gsm).HasPrecision(18, 3);
        builder.Property(g => g.TubFormat).HasMaxLength(100);
        builder.Property(g => g.WindingTapeFormat).HasMaxLength(100);
        builder.Property(g => g.QualityInstructions).HasMaxLength(2000);
        builder.Property(g => g.LabInstructions).HasMaxLength(2000);
        builder.Property(g => g.InternalInstructions).HasMaxLength(2000);
        builder.Property(g => g.CustomerInstructions).HasMaxLength(2000);
        builder.Property(g => g.Notes).HasMaxLength(2000);
        builder.Property(g => g.SpecificationTemplateName).HasMaxLength(200);

        builder.Ignore(g => g.RemainingQuantity);
        builder.Ignore(g => g.PlannedBasinQuantity);
        builder.Ignore(g => g.ProducedBasinQuantity);
        builder.Ignore(g => g.RemainingBasinQuantity);
        builder.Ignore(g => g.HasBasins);

        // Basins (spec sections 10-11) are part of the group aggregate: loaded, written and renumbered
        // with their parent, never edited from the outside. The relationship is declared here so a basin
        // always has exactly one cell as its parent.
        builder.Metadata.FindNavigation(nameof(FormationGroup.Basins))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(g => g.Basins)
            .WithOne()
            .HasForeignKey(b => b.FormationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(g => new { g.FormationRequestId, g.GroupNumber }).IsUnique();
        builder.HasIndex(g => g.SpecificationTemplateId);
    }
}

/// <summary>
/// A basin / detail under a Formation Group (spec sections 10-11). Purely additive: a group that has no basin
/// rows keeps working exactly as before, so no existing Formation Group is rewritten or migrated.
/// </summary>
public class FormationBasinConfiguration : IEntityTypeConfiguration<FormationBasin>
{
    public void Configure(EntityTypeBuilder<FormationBasin> builder)
    {
        builder.ToTable("FormationBasins");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.PlannedQuantity).HasPrecision(18, 3);
        builder.Property(b => b.ProducedQuantity).HasPrecision(18, 3);
        builder.Property(b => b.Unit).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.Color).HasMaxLength(100);
        builder.Property(b => b.WidthCm).HasPrecision(18, 3);
        builder.Property(b => b.MetersPerKg).HasPrecision(18, 4);
        builder.Property(b => b.Gsm).HasPrecision(18, 3);
        builder.Property(b => b.TubFormat).HasMaxLength(100);
        builder.Property(b => b.WindingTapeFormat).HasMaxLength(100);
        builder.Property(b => b.QualityInstructions).HasMaxLength(2000);
        builder.Property(b => b.LabInstructions).HasMaxLength(2000);
        builder.Property(b => b.InternalInstructions).HasMaxLength(2000);
        builder.Property(b => b.CustomerInstructions).HasMaxLength(2000);
        builder.Property(b => b.Notes).HasMaxLength(2000);
        builder.Property(b => b.SpecificationTemplateName).HasMaxLength(200);

        builder.Ignore(b => b.RemainingQuantity);

        builder.HasIndex(b => new { b.FormationGroupId, b.BasinNumber }).IsUnique();
        builder.HasIndex(b => b.SpecificationTemplateId);
    }
}

public class FormationSpecTemplateConfiguration : IEntityTypeConfiguration<FormationSpecTemplate>
{
    public void Configure(EntityTypeBuilder<FormationSpecTemplate> builder)
    {
        builder.ToTable("FormationSpecTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();

        builder.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(t => t.WidthCm).HasPrecision(18, 3);
        builder.Property(t => t.MetersPerKg).HasPrecision(18, 4);
        builder.Property(t => t.Gsm).HasPrecision(18, 3);
        builder.Property(t => t.TubFormat).HasMaxLength(100);
        builder.Property(t => t.WindingTapeFormat).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.QualityInstructions).HasMaxLength(2000);
        builder.Property(t => t.LabInstructions).HasMaxLength(2000);
        builder.Property(t => t.InternalInstructions).HasMaxLength(2000);
        builder.Property(t => t.CustomerInstructions).HasMaxLength(2000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(t => t.IsActive);
    }
}
