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

        // Phase 4: AllowSkip is no longer part of the approved policy fields - the capability was
        // removed from the entity, its configuration and its commands with the legacy skip workflow.
        foreach (var name in new[]
                 {
                     nameof(ProductionStageDefinition.RequiresInputQuantity),
                     nameof(ProductionStageDefinition.RequiresOutputQuantity),
                     nameof(ProductionStageDefinition.Notes)
                 })
        {
            stage.FindProperty(name).Should().NotBeNull($"{name} is required by the stage seeder and the stage commands");
        }

        stage.FindProperty(nameof(ProductionStageDefinition.Notes))!.GetMaxLength().Should().Be(1000);
    }

    // ------------------------------------------------------------- removals

    [Fact]
    public void StageApprovalColumns_AreNoLongerMapped()
    {
        var model = Model();
        var stage = Entity(model, typeof(ProductionStageDefinition));
        var execution = Entity(model, typeof(ProductionOrderStageExecution));

        // Production stage approval was removed by decision: the flags and the
        // approval-tracking columns must never come back under any name.
        stage.FindProperty("RequiresApproval").Should().BeNull();
        execution.FindProperty("ApprovalRequired").Should().BeNull();
        execution.FindProperty("ApprovedBy").Should().BeNull();
        execution.FindProperty("ApprovedAtUtc").Should().BeNull();
    }

    [Fact]
    public void FormationRequestLinkColumns_AreOptionalReferencesOnly()
    {
        var model = Model();
        var order = Entity(model, typeof(ProductionOrder));
        var request = Entity(model, typeof(FormationRequest));

        // The planning reference was re-introduced by the "raw issue creates the Job Order"
        // workflow (it records WHICH formation request/basin an issue came from). It stays
        // strictly OPTIONAL and one-directional, so the two documents remain independent:
        //   * the column is nullable and has NO foreign key to the planning documents,
        //     so a formation request never constrains or cascades into a Job Order;
        //   * a Formation Request still holds no reference back to any Job Order;
        //   * a direct issue (no formation request) is fully valid.
        var formationRequestId = order.FindProperty(nameof(ProductionOrder.FormationRequestId));
        var formationBasinId = order.FindProperty(nameof(ProductionOrder.FormationBasinId));

        formationRequestId.Should().NotBeNull();
        formationBasinId.Should().NotBeNull();
        formationRequestId!.ClrType.Should().Be(typeof(Guid?));
        formationBasinId!.ClrType.Should().Be(typeof(Guid?));
        formationRequestId.IsNullable.Should().BeTrue("a direct issue has no formation request at all");
        formationBasinId.IsNullable.Should().BeTrue();

        order.GetForeignKeys().Should().NotContain(f =>
            f.PrincipalEntityType.Name == nameof(FormationRequest)
            || f.PrincipalEntityType.Name == nameof(FormationBasin),
            "the planning documents must never become a required integrity dependency of an order");

        // Never the other way round: a Formation Request is planning-only and points at nothing.
        request.FindProperty("ProductionOrderId").Should().BeNull();
        request.FindProperty("FormationRequestId").Should().BeNull();
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
