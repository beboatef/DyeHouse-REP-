using DyeHouseERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DyeHouseERP.Persistence.Configurations;

public class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("ProductionOrders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(30).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        builder.Property(o => o.Color).HasMaxLength(100);
        builder.Property(o => o.RawOrigin).HasMaxLength(500);
        builder.Property(o => o.CustomerReference).HasMaxLength(200);
        builder.Property(o => o.Notes).HasMaxLength(2000);
        // Why an order was closed with no output (spec section 18) - kept on the order,
        // not only in the audit log, so it is visible wherever the order is read.
        builder.Property(o => o.ZeroOutputReason).HasMaxLength(1000);
        builder.Property(o => o.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ModifiedBy).HasMaxLength(100);

        builder.Property(o => o.RequestedQuantityKg).HasPrecision(18, 3);
        builder.Property(o => o.RequestedQuantityMeter).HasPrecision(18, 3);

        builder.Property(o => o.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        // Closed line / open line (الخط المقفول / على المفتوح) is real, indexed data -
        // it is filtered, reported, costed and delivered against (spec section 16).
        builder.Property(o => o.JobOrderType).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Job Order costing (spec section 34). Only the ESTIMATE and the
        // APPROVED figure are stored; the ACTUAL cost is always rolled up live
        // from posted transactions so it can never drift from them.
        builder.Property(o => o.EstimatedCost).HasPrecision(18, 2);
        builder.Property(o => o.ApprovedCost).HasPrecision(18, 2);
        builder.Property(o => o.CostApprovedBy).HasMaxLength(100);
        builder.Property(o => o.CostingNotes).HasMaxLength(2000);

        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.ItemId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.JobOrderType);
        builder.HasIndex(o => o.ReprocessingOfProductionOrderId);

        builder.Metadata.FindNavigation(nameof(ProductionOrder.StageExecutions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ProductionOrder.RawAllocations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.StageExecutions)
            .WithOne()
            .HasForeignKey(s => s.ProductionOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.RawAllocations)
            .WithOne()
            .HasForeignKey(a => a.ProductionOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductionOrderStageExecutionConfiguration : IEntityTypeConfiguration<ProductionOrderStageExecution>
{
    public void Configure(EntityTypeBuilder<ProductionOrderStageExecution> builder)
    {
        builder.ToTable("ProductionOrderStageExecutions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Operator).HasMaxLength(100);
        builder.Property(s => s.DrawingNumber).HasMaxLength(100);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        foreach (var qty in new[]
        {
            nameof(ProductionOrderStageExecution.InputKg), nameof(ProductionOrderStageExecution.InputMeter),
            nameof(ProductionOrderStageExecution.OutputKg), nameof(ProductionOrderStageExecution.OutputMeter),
            nameof(ProductionOrderStageExecution.LossKg), nameof(ProductionOrderStageExecution.LossMeter),
            nameof(ProductionOrderStageExecution.SeparatesKg), nameof(ProductionOrderStageExecution.SeparatesMeter)
        })
        {
            builder.Property(qty).HasPrecision(18, 3);
        }

        builder.HasIndex(s => new { s.ProductionOrderId, s.Sequence });
        builder.HasIndex(s => s.StageDefinitionId);

        // Machine / execution-shift linkage (S-Dynamic-Machines, S-Production-Execution-Shifts).
        // The reference lives on the execution and is mapped explicitly so that neither column,
        // nor the constraint that links it to its master data, is ever silently dropped: a
        // completed stage keeps naming the machine and shift it actually ran on. EntityFramework
        // cannot delete a machine/shift that a stage execution still references (NoAction).
        builder.HasIndex(s => s.MachineId);
        builder.HasIndex(s => s.ProductionShiftId);

        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(s => s.MachineId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ProductionShift>()
            .WithMany()
            .HasForeignKey(s => s.ProductionShiftId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class RawAllocationConfiguration : IEntityTypeConfiguration<RawAllocation>
{
    public void Configure(EntityTypeBuilder<RawAllocation> builder)
    {
        builder.ToTable("RawAllocations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.QuantityKg).HasPrecision(18, 3);
        builder.Property(a => a.QuantityMeter).HasPrecision(18, 3);
        builder.Property(a => a.AllocatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(a => a.RawMessageId);
        builder.HasIndex(a => a.ItemId);
    }
}

public class NegativeStockOverrideConfiguration : IEntityTypeConfiguration<NegativeStockOverride>
{
    public void Configure(EntityTypeBuilder<NegativeStockOverride> builder)
    {
        builder.ToTable("NegativeStockOverrides");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.RequestedQuantity).HasPrecision(18, 3);
        builder.Property(o => o.BalanceBefore).HasPrecision(18, 3);
        builder.Property(o => o.ResultingBalance).HasPrecision(18, 3);
        builder.Property(o => o.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(o => o.RequestedBy).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ApprovedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(o => o.RawMessageId);
    }
}
