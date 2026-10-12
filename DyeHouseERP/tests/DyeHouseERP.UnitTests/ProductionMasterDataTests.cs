using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Dynamic production master data (S-Dynamic-Stages, S-Dynamic-Machines,
/// S-Production-Execution-Shifts). These tests pin the approved initial data and
/// the "no code change needed" shape of the model.
/// </summary>
public class ProductionStageSeedTests
{
    [Fact]
    public void ApprovedOperationList_ContainsExactlyTheTwentyOneOperations()
    {
        ProductionStageSeeder.StageNames.Should().HaveCount(21);
        ProductionStageSeeder.ApprovedOperationCount.Should().Be(21);
    }

    [Fact]
    public void ApprovedOperationList_MatchesTheApprovedOperationsExactlyAndInOrder()
    {
        // The agreed initial master data, verbatim. Order is display order only.
        string[] expected =
        {
            "استلام", "التشكيل", "الجاهز", "الجيت", "الرام التثبيت", "الرام التجهيز",
            "الفرادة", "اللف", "انكماش وتكسير", "بيتش", "روتاري", "سوفلينا", "غسيل سوفلينا",
            "عصارة مفتوح", "عصارة مقفول", "كسترا", "كلندر", "كومباكتور", "مثبت حراري",
            "مثبت مائي", "مجفف"
        };

        ProductionStageSeeder.StageNames.Should().Equal(expected);
    }

    [Fact]
    public void ApprovedOperationList_ContainsNoReworkOrShiftVariantRows()
    {
        // Final decision: rework is a capability of every operation and the
        // production execution shift is execution data - neither is a stage row.
        ProductionStageSeeder.StageNames
            .Should().OnlyContain(name => !name.Contains("اعادة") && !name.Contains("وردية"));
    }

    [Fact]
    public void ApprovedOperationList_ContainsNoInventedStage22()
    {
        ProductionStageSeeder.StageNames.Should().NotContain("22");
        ProductionStageSeeder.StageNames.Should().HaveCountLessThan(22);
    }

    [Fact]
    public async Task Seeder_IsIdempotent_AndPreservesTwentyOneStagesPlusPrinting()
    {
        await using var db = NewDb();

        await ProductionStageSeeder.SeedAsync(db, NullLogger.Instance);
        await ProductionStageSeeder.SeedAsync(db, NullLogger.Instance); // re-run must not duplicate

        (await db.ProductionStageDefinitions.CountAsync()).Should().Be(22);
        var printing = await db.ProductionStageDefinitions.SingleAsync(s => s.Code == "PRINTING");
        printing.RequiresDrawingNumber.Should().BeTrue();
        printing.Name.Should().Be("الطباعة");
    }

    [Fact]
    public async Task Seeder_MarksExactlyOneFormationStageAndOneReadyGoodsStage()
    {
        await using var db = NewDb();
        await ProductionStageSeeder.SeedAsync(db, NullLogger.Instance);

        var stages = await db.ProductionStageDefinitions.AsNoTracking().ToListAsync();

        stages.Count(s => s.IsFormationStage).Should().Be(1);
        stages.Count(s => s.IsReadyGoodsStage).Should().Be(1);
        stages.Single(s => s.IsFormationStage).Name.Should().Be("التشكيل");
        stages.Single(s => s.IsReadyGoodsStage).Name.Should().Be("الجاهز");
    }

    [Fact]
    public void NewStageCanBeAdded_WithNoFixedMaximum()
    {
        // "Adding a stage in the future happens from inside the ERP with no source
        // code change" - the entity imposes no count limit and no route.
        var sequence = 22;
        var act = () =>
        {
            for (var i = 0; i < 50; i++)
                _ = new ProductionStageDefinition($"PS{sequence + i:000}", $"مرحلة {sequence + i}", sequence + i, "tester");
        };

        act.Should().NotThrow();
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

public class MachineMasterDataTests
{
    [Fact]
    public void InitialMachineMapping_MatchesTheApprovedNumbers()
    {
        var map = MachineSeeder.MachineMap.ToDictionary(m => m.StageCode, m => m.MachineCount);

        map["PS004"].Should().Be(15); // الجيت
        map["PS005"].Should().Be(4);  // الرام التثبيت
        map["PS006"].Should().Be(4);  // الرام التجهيز
        map["PS020"].Should().Be(2);  // مثبت مائي
        map["PS019"].Should().Be(1);  // مثبت حراري

        MachineSeeder.MachineMap.Sum(m => m.MachineCount).Should().Be(26);
    }

    [Fact]
    public async Task Seeder_CreatesMachinesAndStageLinks_AndIsIdempotent()
    {
        await using var db = NewDb();
        await ProductionStageSeeder.SeedAsync(db, NullLogger.Instance);
        await MachineSeeder.SeedAsync(db, NullLogger.Instance);
        await MachineSeeder.SeedAsync(db, NullLogger.Instance); // re-run must not duplicate

        (await db.Machines.CountAsync()).Should().Be(26);
        (await db.StageMachines.CountAsync()).Should().Be(26);
    }

    [Fact]
    public void Machine_RequiresCodeAndName()
    {
        var noCode = () => new Machine("  ", "Machine", "tester");
        var noName = () => new Machine("MC900", "  ", "tester");

        noCode.Should().Throw<ArgumentException>();
        noName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeactivatedMachine_StaysInMasterData_SoHistorySurvives()
    {
        var machine = new Machine("MC900", "Machine 900", "tester");

        machine.Deactivate("tester");

        machine.IsActive.Should().BeFalse();
        machine.Activate("tester");
        machine.IsActive.Should().BeTrue();
    }

    [Fact]
    public void StageMachine_RequiresBothSides()
    {
        var noStage = () => new StageMachine(Guid.Empty, Guid.NewGuid());
        var noMachine = () => new StageMachine(Guid.NewGuid(), Guid.Empty);

        noStage.Should().Throw<ArgumentException>();
        noMachine.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AStageCanHaveSeveralMachines_AndOneMachineCanServeSeveralStages()
    {
        var stageA = Guid.NewGuid();
        var stageB = Guid.NewGuid();
        var machine1 = Guid.NewGuid();
        var machine2 = Guid.NewGuid();

        var links = new[]
        {
            new StageMachine(stageA, machine1),
            new StageMachine(stageA, machine2),
            new StageMachine(stageB, machine1)
        };

        links.Count(l => l.StageDefinitionId == stageA).Should().Be(2);
        links.Count(l => l.MachineId == machine1).Should().Be(2);
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

public class ProductionExecutionShiftTests
{
    [Fact]
    public async Task Seeder_CreatesTheThreeInitialShifts_AndIsIdempotent()
    {
        await using var db = NewDb();

        await ProductionShiftSeeder.SeedAsync(db, NullLogger.Instance);
        await ProductionShiftSeeder.SeedAsync(db, NullLogger.Instance);

        var shifts = await db.ProductionShifts.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();

        shifts.Should().HaveCount(3);
        shifts.Select(s => s.Sequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Shift_HasNoHoursOrRestDayFields_ItIsExecutionDataOnly()
    {
        // Production execution shifts are deliberately NOT the HR shift model: no
        // start/end time, no hours, no working/rest days, no attendance, no payroll.
        var shift = new ProductionShift("SHIFT-4", "الوردية الرابعة", 4, "tester");

        var propertyNames = typeof(ProductionShift).GetProperties().Select(p => p.Name).ToList();

        propertyNames.Should().NotContain("StartTime");
        propertyNames.Should().NotContain("EndTime");
        propertyNames.Should().NotContain("Hours");
        propertyNames.Should().NotContain("WorkingDays");
        propertyNames.Should().NotContain("RestDays");
        propertyNames.Should().NotContain("Overtime");
        propertyNames.Should().NotContain("DepartmentId");
        shift.Sequence.Should().Be(4);
    }

    [Fact]
    public void Shifts_AreNotLimitedToThree()
    {
        var act = () =>
        {
            for (var i = 4; i <= 10; i++)
                _ = new ProductionShift($"SHIFT-{i}", $"الوردية {i}", i, "tester");
        };

        act.Should().NotThrow();
    }

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
