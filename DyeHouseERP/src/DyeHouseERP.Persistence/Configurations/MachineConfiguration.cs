using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

/// <summary>
/// Dynamic machine master data + its stage mapping (S-Dynamic-Machines).
/// Everything here is ordinary master data: no fixed count, no hard-coded
/// machine list, and a deactivated machine is kept for history.
/// </summary>
public class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("Machines");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Code).HasMaxLength(30).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(1000);
        builder.Property(m => m.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(m => m.Code).IsUnique();
        builder.HasIndex(m => m.IsActive);

        builder.Metadata.FindNavigation(nameof(Machine.StageLinks))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(m => m.StageLinks)
            .WithOne()
            .HasForeignKey(l => l.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Machine ↔ stage association (many-to-many, S-Dynamic-Machines).
/// A machine may serve several stages and a stage may have several machines.
/// The (StageDefinitionId, MachineId) pair is unique so the same association can
/// never be recorded twice.
/// </summary>
public class StageMachineConfiguration : IEntityTypeConfiguration<StageMachine>
{
    public void Configure(EntityTypeBuilder<StageMachine> builder)
    {
        builder.ToTable("StageMachines");
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.StageDefinitionId, l.MachineId }).IsUnique();
        builder.HasIndex(l => l.MachineId);

        builder.HasOne<ProductionStageDefinition>()
            .WithMany()
            .HasForeignKey(l => l.StageDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Production EXECUTION shift master data (S-Production-Execution-Shifts).
/// Deliberately separate from the HR employee shift model.
/// </summary>
public class ProductionShiftConfiguration : IEntityTypeConfiguration<ProductionShift>
{
    public void Configure(EntityTypeBuilder<ProductionShift> builder)
    {
        builder.ToTable("ProductionShifts");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(30).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.Sequence);
    }
}
