using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class ProductionOrderTests
{
    private static ProductionStageDefinition Stage(string code, int sequence, bool allowSkip = false, bool requiresApproval = false) =>
        new(code, code, sequence, "tester", allowSkip: allowSkip, requiresApproval: requiresApproval);

    private static ProductionOrder CreateOrder() =>
        new("PRD-2026-000001", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 1000m);

    [Fact]
    public void BuildStageRoute_CreatesOneExecutionPerActiveStage_InSequenceOrder()
    {
        var order = CreateOrder();
        var stages = new[] { Stage("B", 2), Stage("A", 1), Stage("C", 3) };

        order.BuildStageRoute(stages);

        order.StageExecutions.Select(s => s.Sequence).Should().BeInAscendingOrder();
        order.StageExecutions.Should().HaveCount(3);
        order.StageExecutions.Should().OnlyContain(s => s.Status == StageExecutionStatus.Pending);
    }

    [Fact]
    public void BuildStageRoute_CalledTwice_Throws()
    {
        var order = CreateOrder();
        order.BuildStageRoute(new[] { Stage("A", 1) });

        var act = () => order.BuildStageRoute(new[] { Stage("A", 1) });

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AllocateRaw_FromDraft_MovesStatusToRawAllocated()
    {
        var order = CreateOrder();

        order.AllocateRaw(Guid.NewGuid(), Guid.NewGuid(), quantityKg: 600m, quantityMeter: null, allocatedBy: "tester");

        order.Status.Should().Be(ProductionOrderStatus.RawAllocated);
    }

    [Fact]
    public void AllocateRaw_TwiceFromDifferentMessages_RecordsBothAllocationsIndependently()
    {
        var order = CreateOrder();
        var message125 = Guid.NewGuid();
        var message131 = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        order.AllocateRaw(message125, itemId, quantityKg: 400m, quantityMeter: null, allocatedBy: "tester");
        order.AllocateRaw(message131, itemId, quantityKg: 200m, quantityMeter: null, allocatedBy: "tester");

        order.RawAllocations.Should().HaveCount(2);
        order.RawAllocations.Should().Contain(a => a.RawMessageId == message125 && a.QuantityKg == 400m);
        order.RawAllocations.Should().Contain(a => a.RawMessageId == message131 && a.QuantityKg == 200m);
    }

    [Fact]
    public void MarkInProduction_WhileStillDraft_Throws()
    {
        var order = CreateOrder();

        var act = order.MarkInProduction;

        act.Should().Throw<DomainException>("raw material must be allocated before production can start");
    }

    [Fact]
    public void Complete_WithPendingStages_Throws()
    {
        var order = CreateOrder();
        order.BuildStageRoute(new[] { Stage("A", 1) });

        var act = order.Complete;

        act.Should().Throw<DomainException>();
    }
}

public class ProductionOrderStageExecutionTests
{
    private static ProductionOrderStageExecution NewExecution() => new ProductionOrder(
            "PRD-2026-000002", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester")
        .Also(o => o.BuildStageRoute(new[] { new ProductionStageDefinition("A", "A", 1, "tester") }))
        .StageExecutions.Single();

    [Fact]
    public void Complete_WhenApprovalRequiredButNotProvided_Throws()
    {
        var execution = NewExecution();
        execution.Start("operator");

        var act = () => execution.Complete(
            inputKg: 100m, inputMeter: null, outputKg: 90m, outputMeter: null,
            lossKg: 10m, lossMeter: null, separatesKg: null, separatesMeter: null,
            notes: null, approvalRequired: true, approvedBy: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Skip_WhenStageDoesNotAllowSkip_Throws()
    {
        var execution = NewExecution();

        var act = () => execution.Skip(stageAllowsSkip: false, reason: "test", modifiedBy: "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Start_ThenComplete_TransitionsThroughExpectedStatuses()
    {
        var execution = NewExecution();

        execution.Status.Should().Be(StageExecutionStatus.Pending);
        execution.Start("operator");
        execution.Status.Should().Be(StageExecutionStatus.InProgress);

        execution.Complete(100m, null, 90m, null, 10m, null, null, null, null, approvalRequired: false, approvedBy: null);
        execution.Status.Should().Be(StageExecutionStatus.Completed);
    }
}

internal static class TestExtensions
{
    // small fluent helper so the private stage-execution factory above stays a one-liner
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
