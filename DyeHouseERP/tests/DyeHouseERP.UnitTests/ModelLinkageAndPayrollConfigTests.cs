using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Pins the EF model decisions this release depends on, so a configuration file can
/// never quietly dilute them again:
///
///   * the machine / production-shift execution linkage is preserved (the columns and
///     the constraints that link them to their master data must stay mapped, even
///     though the current write path does not set them);
///   * the ready-goods transfer keeps its explicit reference to the stage execution;
///   * the production-stage policy fields the seeder/commands require stay mapped;
///   * the payroll amounts keep 18,4 (day/hour counts 9,2), the workflow strings keep
///     their 100/1000 limits, the reversal indexes exist and the one-run-per-period
///     index filter still excludes BOTH cancelled and reversed runs.
///
/// A model-only test: no database migration, no SQL Server and no relational provider
/// are involved, which is exactly why it can run in this environment.
/// </summary>
public class ModelLinkageAndPayrollConfigTests
{
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new ApplicationDbContext(options);
        return db.Model;
    }

    private static IEntityType Entity(IModel model, Type type) =>
        model.FindEntityType(type) ?? throw new Xunit.Sdk.XunitException($"Entity {type.Name} is not part of the model.");

    // ---------------------------------------------------------------- executions

    [Fact]
    public void StageExecution_KeepsTheMachineAndProductionShiftLinkage()
    {
        var model = Model();
        var execution = Entity(model, typeof(ProductionOrderStageExecution));
        var machine = Entity(model, typeof(Machine));
        var shift = Entity(model, typeof(ProductionShift));

        // The columns themselves: nullable, so a stage that recorded neither is still valid.
        var machineId = execution.FindProperty(nameof(ProductionOrderStageExecution.MachineId));
        var shiftId = execution.FindProperty(nameof(ProductionOrderStageExecution.ProductionShiftId));

        machineId.Should().NotBeNull("the historical MachineId column must never be dropped");
        shiftId.Should().NotBeNull("the historical ProductionShiftId column must never be dropped");
        machineId!.ClrType.Should().Be(typeof(Guid?));
        shiftId!.ClrType.Should().Be(typeof(Guid?));
        machineId.IsNullable.Should().BeTrue();
        shiftId.IsNullable.Should().BeTrue();

        // Indexed, because both are filtered/reported on.
        execution.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(ProductionOrderStageExecution.MachineId)));
        execution.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(ProductionOrderStageExecution.ProductionShiftId)));

        // Really linked to the master data, and never cascade-deleted: deleting a machine
        // must not silently erase the execution history that names it.
        var machineFk = execution.GetForeignKeys().SingleOrDefault(f => f.PrincipalEntityType.Name == machine.Name);
        var shiftFk = execution.GetForeignKeys().SingleOrDefault(f => f.PrincipalEntityType.Name == shift.Name);

        machineFk.Should().NotBeNull("the execution must reference the machine it ran on");
        shiftFk.Should().NotBeNull("the execution must reference the shift it ran in");
        machineFk!.Properties.Single().Name.Should().Be(nameof(ProductionOrderStageExecution.MachineId));
        shiftFk!.Properties.Single().Name.Should().Be(nameof(ProductionOrderStageExecution.ProductionShiftId));
        machineFk.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);
        shiftFk.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);
    }

    [Fact]
    public void ReadyGoodsTransfer_KeepsItsStageExecutionReference()
    {
        var model = Model();
        var transfer = Entity(model, typeof(ReadyGoodsTransfer));
        var execution = Entity(model, typeof(ProductionOrderStageExecution));

        var fk = transfer.GetForeignKeys()
            .SingleOrDefault(f => f.PrincipalEntityType.Name == execution.Name);

        fk.Should().NotBeNull("a ready-goods lot must still trace back to the stage that produced it");
        fk!.Properties.Single().Name.Should().Be(nameof(ReadyGoodsTransfer.StageExecutionId));
        fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    // ------------------------------------------------------- production stages

    [Fact]
    public void ProductionStageDefinition_KeepsTheApprovedPolicyFields()
    {
        var model = Model();
        var stage = Entity(model, typeof(ProductionStageDefinition));

        foreach (var name in new[]
                 {
                     nameof(ProductionStageDefinition.RequiresApproval),
                     nameof(ProductionStageDefinition.AllowSkip),
                     nameof(ProductionStageDefinition.AllowRepeat),
                     nameof(ProductionStageDefinition.AllowRework),
                     nameof(ProductionStageDefinition.AllowReturn),
                     nameof(ProductionStageDefinition.RequiresInputQuantity),
                     nameof(ProductionStageDefinition.RequiresOutputQuantity),
                     nameof(ProductionStageDefinition.Notes)
                 })
        {
            stage.FindProperty(name).Should().NotBeNull($"{name} is required by the stage seeder and the stage commands");
        }

        stage.FindProperty(nameof(ProductionStageDefinition.Notes))!.GetMaxLength().Should().Be(1000);
    }

    [Fact]
    public void StageExecution_KeepsTheApprovalTrackingColumns()
    {
        var model = Model();
        var execution = Entity(model, typeof(ProductionOrderStageExecution));

        execution.FindProperty(nameof(ProductionOrderStageExecution.ApprovalRequired)).Should().NotBeNull();
        execution.FindProperty(nameof(ProductionOrderStageExecution.ApprovedBy)).Should().NotBeNull();
        execution.FindProperty(nameof(ProductionOrderStageExecution.ApprovedAtUtc)).Should().NotBeNull();
        execution.FindProperty(nameof(ProductionOrderStageExecution.ApprovedBy))!.GetMaxLength().Should().Be(100);
    }

    // ---------------------------------------------------------------- payroll

    [Fact]
    public void PayrollRunLine_KeepsMonetaryPrecisionAndDayPrecision()
    {
        var model = Model();
        var line = Entity(model, typeof(PayrollRunLine));

        // Money keeps four decimals - the payroll calculator rounds to four, so SQL Server
        // must not truncate it to two.
        foreach (var name in new[]
                 {
                     nameof(PayrollRunLine.BasicSalary), nameof(PayrollRunLine.Allowances),
                     nameof(PayrollRunLine.Incentives), nameof(PayrollRunLine.Bonuses),
                     nameof(PayrollRunLine.Commissions), nameof(PayrollRunLine.OvertimeAmount),
                     nameof(PayrollRunLine.OtherEarnings), nameof(PayrollRunLine.Deductions),
                     nameof(PayrollRunLine.Tax), nameof(PayrollRunLine.Insurance),
                     nameof(PayrollRunLine.EmployerInsurance), nameof(PayrollRunLine.Advances)
                 })
        {
            var property = line.FindProperty(name)!;
            property.GetPrecision().Should().Be(18, $"{name} is a monetary amount");
            property.GetScale().Should().Be(4, $"{name} is a monetary amount");
        }

        foreach (var name in new[]
                 {
                     nameof(PayrollRunLine.AttendanceDays), nameof(PayrollRunLine.OvertimeHours),
                     nameof(PayrollRunLine.LeaveDays)
                 })
        {
            var property = line.FindProperty(name)!;
            property.GetPrecision().Should().Be(9, $"{name} is a day/hour count");
            property.GetScale().Should().Be(2, $"{name} is a day/hour count");
        }

        line.FindProperty(nameof(PayrollRunLine.Notes))!.GetMaxLength().Should().Be(1000);
        line.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(PayrollRunLine.CostCenterId)));
        line.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(PayrollRunLine.HrSectionId)));
    }

    [Fact]
    public void PayrollRun_KeepsWorkflowStringLimitsAndReversalIndexes()
    {
        var model = Model();
        var run = Entity(model, typeof(PayrollRun));

        foreach (var name in new[]
                 {
                     nameof(PayrollRun.CalculatedBy), nameof(PayrollRun.SubmittedBy),
                     nameof(PayrollRun.ReviewedBy), nameof(PayrollRun.ReversedBy)
                 })
        {
            run.FindProperty(name)!.GetMaxLength().Should().Be(100, $"{name} is a \"who\" field");
        }

        foreach (var name in new[] { nameof(PayrollRun.ReviewNotes), nameof(PayrollRun.ReversalReason) })
        {
            run.FindProperty(name)!.GetMaxLength().Should().Be(1000, $"{name} is a \"why\" field");
        }

        run.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(PayrollRun.ReversesPayrollRunId)));
        run.GetIndexes().Should().Contain(i => i.Properties.Any(p => p.Name == nameof(PayrollRun.ReversedByPayrollRunId)));
    }

    [Fact]
    public void PayrollRun_PeriodIndexExcludesBothCancelledAndReversedRuns()
    {
        var model = Model();
        var run = Entity(model, typeof(PayrollRun));

        var periodIndex = run.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(PayrollRun.PeriodYear), nameof(PayrollRun.PeriodMonth) }));

        periodIndex.IsUnique.Should().BeTrue();

        var filter = periodIndex.GetFilter();
        filter.Should().NotBeNull("the one-active-run-per-period index must be filtered");
        filter.Should().Contain("Cancelled");
        filter.Should().Contain("Reversed");
    }
}
