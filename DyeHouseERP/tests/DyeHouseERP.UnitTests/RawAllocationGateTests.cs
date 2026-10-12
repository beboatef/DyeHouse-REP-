using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H1: production may not start - and no production transition may record output,
/// create ready-goods stock or complete the order - until 100% of the requested
/// quantity is allocated.
///
/// The rule is expressed once on the aggregate and read as a pure decision here:
///   * 0%   -> refuses
///   * 50%  -> refuses
///   * 100% -> allows
///   * KG and Meter are INDEPENDENT units: no allocation in one unit ever satisfies
///     a requirement in the other, and there is no conversion between them.
/// These tests pin the decision itself; the handler tests pin that every productive
/// path actually consults it.
/// </summary>
public class RawAllocationGateTests
{
    private static ProductionOrder Order(decimal? kg = null, decimal? meter = null, Guid? reprocessingOf = null) =>
        new("PRD-2026-000001", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: kg, requestedQuantityMeter: meter,
            reprocessingOfProductionOrderId: reprocessingOf);

    private static RawAllocation Allocation(ProductionOrder order, decimal? kg, decimal? meter) =>
        order.AllocateRaw(Guid.NewGuid(), Guid.NewGuid(), kg, meter, "tester");

    // ------------------------------------------------------- 0 / 50 / 100 %

    [Fact]
    public void ZeroPercent_IsRefused()
    {
        var order = Order(kg: 1000m);

        order.HasCompleteRawAllocation.Should().BeFalse();
        order.AllocatedQuantityKg.Should().Be(0m);

        Action act = () => order.EnsureRawAllocationComplete();

        act.Should().Throw<DomainException>()
            .WithMessage("*not fully allocated*")
            .WithMessage("*0 of 1000 KG*", "the message must name the real shortfall");
    }

    [Fact]
    public void FiftyPercent_IsRefused()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 500m, null);

        order.HasCompleteRawAllocation.Should().BeFalse();

        Action act = () => order.EnsureRawAllocationComplete();

        act.Should().Throw<DomainException>().WithMessage("*500 of 1000 KG*");
    }

    [Fact]
    public void OneHundredPercent_IsAllowed()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 1000m, null);

        order.HasCompleteRawAllocation.Should().BeTrue();
        Action act = () => order.EnsureRawAllocationComplete();
        act.Should().NotThrow();
    }

    [Fact]
    public void OverAllocation_IsAllowed_BecauseNoUpperBoundRuleIsInventedHere()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 1200m, null);

        // The existing validation rules bound a single allocation by the lot's balance;
        // nothing in the business rules caps the total at the requested quantity, and
        // this fix deliberately does not invent such a cap.
        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    // ------------------------------------------------ multi-movement completion

    [Fact]
    public void AnAllocationCompletedBySeveralMovements_IsComplete()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 400m, null);
        order.HasCompleteRawAllocation.Should().BeFalse("400 of 1000 is still short");

        Allocation(order, 600m, null);

        order.AllocatedQuantityKg.Should().Be(1000m);
        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    [Fact]
    public void AllocationsInBothUnits_AreSummedPerUnit_NotTogether()
    {
        var order = Order(kg: 1000m, meter: 500m);
        Allocation(order, 600m, 200m);
        Allocation(order, 400m, 300m);

        order.AllocatedQuantityKg.Should().Be(1000m);
        order.AllocatedQuantityMeter.Should().Be(500m);
        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    // ------------------------------------------------------ KG vs Meter

    [Fact]
    public void MeterAllocation_NeverCoversAKgRequirement()
    {
        var order = Order(kg: 1000m);
        Allocation(order, null, 1000m);

        order.AllocatedQuantityMeter.Should().Be(1000m);
        order.HasCompleteRawAllocation.Should().BeFalse(
            "Meter is not KG - there is no conversion between the two units");

        Action act = () => order.EnsureRawAllocationComplete();

        act.Should().Throw<DomainException>().WithMessage("*0 of 1000 KG*");
    }

    [Fact]
    public void KgAllocation_NeverCoversAMeterRequirement()
    {
        var order = Order(meter: 500m);
        Allocation(order, 500m, null);

        order.HasCompleteRawAllocation.Should().BeFalse();

        Allocation(order, null, 500m);

        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    [Fact]
    public void AnOrderRequestingBothUnits_NeedsEachOneCoveredOnItsOwn()
    {
        var order = Order(kg: 1000m, meter: 500m);
        Allocation(order, 1000m, 250m);

        order.HasCompleteRawAllocation.Should().BeFalse("half the Meter is still outstanding");

        Allocation(order, null, 250m);

        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    // ------------------------------------------------- existing-rule details

    [Fact]
    public void AZeroQuantityAllocation_IsNotCoverage()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 0m, null);

        order.RawAllocations.Should().HaveCount(1, "the row itself is legal (only both units null is refused)");
        order.HasCompleteRawAllocation.Should().BeFalse(
            "the rule counts allocated quantity, never the mere existence of an allocation");
    }

    [Fact]
    public void AnOrderWithNoRecordedRequestedQuantity_StillMayNotRunOnNothing()
    {
        var order = Order(); // both units null - only possible for data predating the validators

        order.HasCompleteRawAllocation.Should().BeFalse();

        Allocation(order, 0m, null);
        order.HasCompleteRawAllocation.Should().BeFalse();

        Allocation(order, 10m, null);
        order.HasCompleteRawAllocation.Should().BeTrue("some real raw material is allocated");
    }

    [Fact]
    public void RoundingDustInTheStoredFigures_DoesNotRefuseAFullAllocation()
    {
        var order = Order(kg: 1000m);
        Allocation(order, 999.999m, null);

        // The same 0.001 tolerance the ready-goods cap uses.
        order.HasCompleteRawAllocation.Should().BeTrue();
    }

    [Fact]
    public void ClosingAStage_ChangesNothingAboutTheAllocationDecision()
    {
        var order = Order(kg: 1000m);
        order.BuildStageRoute(
            new[] { new ProductionStageDefinition("PS001", "التشكيل", 1, "tester", isFormationStage: true) },
            "tester");
        order.StageExecutions.Single().CloseWithOutput(950m, null, null, null, null, "operator");

        // Recorded output is NOT evidence that the raw was allocated.
        order.HasCompleteRawAllocation.Should().BeFalse();
        order.AllocatedQuantityKg.Should().Be(0m);
    }

    [Fact]
    public void AReprocessingOrder_IsSubjectToTheSameRule()
    {
        var order = Order(kg: 800m, reprocessingOf: Guid.NewGuid());
        Allocation(order, 400m, null);

        order.HasCompleteRawAllocation.Should().BeFalse();

        Allocation(order, 400m, null);

        order.HasCompleteRawAllocation.Should().BeTrue();
    }
}
