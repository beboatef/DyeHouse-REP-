using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 34: the APPROVED cost is an explicit, attributable sign-off on the LIVE actual rollup, which is
/// derived entirely from posted transactions. Approving before the order is completed would freeze a figure the
/// order's own remaining work can still change - so the completion prerequisite is a genuine domain invariant,
/// not an arbitrary lock. Re-approving a completed order (a later, deliberate correction) stays possible, and
/// each approval records exactly who signed it and when.
/// </summary>
public class CostApprovalOrderingTests
{
    private static ProductionOrder NewOrder() =>
        new("PRD-1", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "planner");

    [Fact]
    public void Cost_CannotBeApproved_BeforeTheOrderIsCompleted()
    {
        var order = NewOrder();
        order.Status.Should().Be(DyeHouseERP.Domain.Enums.ProductionOrderStatus.Draft);

        var act = () => order.ApproveCost(1000m, "premature", "accountant");
        act.Should().Throw<DomainException>().WithMessage("*only be approved once the order is completed*");
    }

    [Fact]
    public void Cost_IsApprovable_OnceCompleted_AndRecordsWhoAndWhen()
    {
        var order = NewOrder();
        order.Complete();

        order.ApproveCost(1000m, "signed off", "accountant");

        order.ApprovedCost.Should().Be(1000m);
        order.CostApprovedBy.Should().Be("accountant");
        order.CostApprovedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ACompletedOrder_Cost_CanBeReApproved_AsADeliberateCorrection()
    {
        var order = NewOrder();
        order.Complete();
        order.ApproveCost(1000m, "first", "accountant");

        order.ApproveCost(1200m, "revised after a late cost entry", "manager");

        order.ApprovedCost.Should().Be(1200m);
        order.CostApprovedBy.Should().Be("manager");
    }

    [Fact]
    public void Estimate_IsEditableWhileOpen_ButFrozenOnceCompleted()
    {
        var order = NewOrder();
        order.SetEstimatedCost(500m, "quote", "planner");
        order.EstimatedCost.Should().Be(500m);

        order.Complete();
        var act = () => order.SetEstimatedCost(600m, "late", "planner");
        act.Should().Throw<DocumentLockedException>();
    }
}
