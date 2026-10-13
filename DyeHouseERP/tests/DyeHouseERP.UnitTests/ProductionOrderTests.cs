using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class ProductionOrderTests
{
    private static ProductionStageDefinition Stage(
        string code, int sequence, bool allowSkip = false,
        bool isFormationStage = false, bool isReadyGoodsStage = false) =>
        new(code, code, sequence, "tester", allowSkip: allowSkip,
            isFormationStage: isFormationStage, isReadyGoodsStage: isReadyGoodsStage);

    private static ProductionOrder CreateOrder() =>
        new("PRD-2026-000001", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 1000m);

    [Fact]
    public void BuildStageRoute_StartsOnlyTheFormationStage_AndLeavesTheRestUndecided()
    {
        var order = CreateOrder();
        var stages = new[] { Stage("B", 2), Stage("A", 1, isFormationStage: true), Stage("C", 3) };

        order.BuildStageRoute(stages, "tester");

        // Spec sections 12-13: the route is NOT pre-built. Only التشكيل exists,
        // and it is already running - every later stage is the user's choice.
        order.StageExecutions.Should().HaveCount(1);
        order.StageExecutions.Single().Status.Should().Be(StageExecutionStatus.InProgress);
        order.StageExecutions.Single().StageDefinitionId
            .Should().Be(stages.Single(s => s.IsFormationStage).Id);
    }

    [Fact]
    public void BuildStageRoute_UsesTheActualJobOrderWeightAsTheFirstBaseline()
    {
        var order = CreateOrder(); // requestedQuantityKg: 1000

        order.BuildStageRoute(new[] { Stage("A", 1, isFormationStage: true) }, "tester");

        order.StageExecutions.Single().BaselineKg.Should().Be(1000m);
    }

    [Fact]
    public void BuildStageRoute_WithoutAFormationStage_Throws()
    {
        var order = CreateOrder();
        var stages = new[] { Stage("A", 1), Stage("B", 2) };

        // Nothing is inferred from Sequence: an unmarked configuration is refused
        // rather than silently starting the order at whichever stage happens to
        // be first in the list.
        var act = () => order.BuildStageRoute(stages, "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void BuildStageRoute_CalledTwice_Throws()
    {
        var order = CreateOrder();
        order.BuildStageRoute(new[] { Stage("A", 1, isFormationStage: true) }, "tester");

        var act = () => order.BuildStageRoute(new[] { Stage("A", 1, isFormationStage: true) }, "tester");

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
    public void Complete_WithAStageStillRunning_Throws()
    {
        var order = CreateOrder();
        order.BuildStageRoute(new[] { Stage("A", 1, isFormationStage: true) }, "tester");

        // The formation stage is still InProgress, so the order is not done.
        var act = order.Complete;

        act.Should().Throw<DomainException>();
    }
}

public class ProductionOrderStageExecutionTests
{
    private static ProductionOrderStageExecution NewExecution() => new ProductionOrder(
            "PRD-2026-000002", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester")
        .Also(o => o.BuildStageRoute(new[] { new ProductionStageDefinition("A", "A", 1, "tester", isFormationStage: true) }, "tester"))
        .StageExecutions.Single();

    [Fact]
    public void Skip_WhenStageDoesNotAllowSkip_Throws()
    {
        var execution = NewExecution();

        var act = () => execution.Skip(stageAllowsSkip: false, reason: "test", modifiedBy: "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void FirstStage_IsAlreadyRunning_ThenCompletes()
    {
        var execution = NewExecution();

        // The formation stage activates itself when the Job Order is created, so
        // there is no separate "start" step for it (spec section 12).
        execution.Status.Should().Be(StageExecutionStatus.InProgress);

        execution.Complete(100m, null, 90m, null, 10m, null, null, null, null);
        execution.Status.Should().Be(StageExecutionStatus.Completed);
    }

    [Fact]
    public void Transfer_RecordsLossAndLossPercentAgainstTheImmutableBaseline()
    {
        // Spec section 15's worked example: baseline 783, output 775 -> 8 KG loss, 1.02%.
        var order = new ProductionOrder(
            "PRD-2026-000003", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 783m);
        var formation = new ProductionStageDefinition("A", "Formation", 1, "tester", isFormationStage: true);
        order.BuildStageRoute(new[] { formation }, "tester");

        var first = order.StageExecutions.Single();
        first.CloseWithOutput(775m, null, null, null, null, "operator");

        first.BaselineKg.Should().Be(783m);
        first.OutputKg.Should().Be(775m);
        first.LossKg.Should().Be(8m);
        first.LossPercentKg.Should().Be(1.02m);
    }

    [Fact]
    public void EditingTheOutput_DoesNotMoveTheBaseline()
    {
        // The spec's second worked example: output edited 775 -> 778 before
        // transfer, so the loss recomputes against the ORIGINAL 783 baseline.
        var order = new ProductionOrder(
            "PRD-2026-000004", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 783m);
        order.BuildStageRoute(
            new[] { new ProductionStageDefinition("A", "Formation", 1, "tester", isFormationStage: true) }, "tester");

        var first = order.StageExecutions.Single();
        first.UpdateOutputWhileOpen(775m, null);
        first.UpdateOutputWhileOpen(778m, null);
        first.CloseWithOutput(778m, null, null, null, null, "operator");

        first.BaselineKg.Should().Be(783m);
        first.LossKg.Should().Be(5m);
        first.LossPercentKg.Should().Be(0.64m);
    }

    [Fact]
    public void AClosedStageIsLockedAgainstFurtherOutputEdits()
    {
        var order = new ProductionOrder(
            "PRD-2026-000005", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 800m);
        order.BuildStageRoute(
            new[] { new ProductionStageDefinition("A", "Formation", 1, "tester", isFormationStage: true) }, "tester");

        var first = order.StageExecutions.Single();
        first.CloseWithOutput(790m, null, null, null, null, "operator");

        var act = () => first.UpdateOutputWhileOpen(999m, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TransferringActivatesOnlyTheChosenStage_BaselinedOnThePreviousOutput()
    {
        var order = new ProductionOrder(
            "PRD-2026-000006", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 800m);
        var formation = new ProductionStageDefinition("A", "Formation", 1, "tester", isFormationStage: true);
        var dyeing = new ProductionStageDefinition("B", "Dyeing", 2, "tester");
        order.BuildStageRoute(new[] { formation }, "tester");

        var first = order.StageExecutions.Single();
        first.CloseWithOutput(775m, null, null, null, null, "operator");
        order.ActivateNextStage(dyeing, 775m, null, "operator");

        // Only ONE stage was added - the route after it stays open until the user
        // picks again (spec section 13).
        order.StageExecutions.Should().HaveCount(2);
        order.StageExecutions.Last().StageDefinitionId.Should().Be(dyeing.Id);
        order.StageExecutions.Last().BaselineKg.Should().Be(775m);
        order.StageExecutions.Last().Status.Should().Be(StageExecutionStatus.InProgress);
        // The closed stage keeps its history and is locked.
        order.StageExecutions.First().Status.Should().Be(StageExecutionStatus.Completed);
        order.StageExecutions.First().OutputKg.Should().Be(775m);
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

/// <summary>
/// The order's editable surface (spec sections 12 + 16). Edits are allowed while the order is open and
/// are refused once it is Completed or Cancelled - never a blanket immutable-after-save rule, but also
/// never a rewrite of the order's identity, its requested quantity (the formation stage's baseline) or
/// anything already posted.
/// </summary>
public class ProductionOrderEditTests
{
    private static ProductionOrder OpenOrder()
    {
        var formation = new ProductionStageDefinition("A", "Formation", 1, "tester",
            allowSkip: true, isFormationStage: true);
        var order = new ProductionOrder("PRD-2026-000900", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            color: "Navy", requestedQuantityKg: 1000m, rawOrigin: "Lot A",
            customerReference: "CR-1", notes: "first note");
        order.BuildStageRoute(new[] { formation }, "tester");
        return order;
    }

    [Fact]
    public void UpdateDetails_EditsTheDescriptiveAndSchedulingFields_WhileTheOrderIsOpen()
    {
        var order = OpenOrder();
        var newDate = DateTime.UtcNow.Date.AddDays(-2);

        order.UpdateDetails("Red", "Lot B", "CR-2", "corrected note", ProductionPriority.High, newDate, "planner");

        order.Color.Should().Be("Red");
        order.RawOrigin.Should().Be("Lot B");
        order.CustomerReference.Should().Be("CR-2");
        order.Notes.Should().Be("corrected note");
        order.Priority.Should().Be(ProductionPriority.High);
        order.OrderDate.Should().Be(newDate);
        order.ModifiedBy.Should().Be("planner");
    }

    [Fact]
    public void UpdateDetails_LeavesTheIdentityAndTheRequestedQuantityUntouched()
    {
        var order = OpenOrder();
        var customerId = order.CustomerId;
        var itemId = order.ItemId;

        order.UpdateDetails("Red", null, null, null, ProductionPriority.Low, DateTime.UtcNow, "planner");

        order.CustomerId.Should().Be(customerId, "the order's identity is never re-pointed by an edit");
        order.ItemId.Should().Be(itemId);
        order.RequestedQuantityKg.Should().Be(1000m);
        order.StageExecutions.Single().BaselineKg.Should().Be(1000m,
            "the formation stage's baseline is the requested quantity and is never desynced by an edit");
    }

    [Fact]
    public void UpdateDetails_AndTheLineType_AreStillAllowedOnceProductionHasStarted()
    {
        var order = OpenOrder();
        order.AllocateRaw(Guid.NewGuid(), order.ItemId, 1000m, null, "operator");
        order.MarkInProduction();
        order.Status.Should().Be(ProductionOrderStatus.InProduction);

        order.UpdateDetails("Red", null, null, "note", ProductionPriority.High, DateTime.UtcNow, "planner");
        order.Color.Should().Be("Red", "descriptive fields are ordinary planning data, not production history");

        // The line type is a CLASSIFICATION of the order - nothing posted is derived from it - so a
        // wrong value is corrected by an ordinary, audited edit while the order is still running.
        order.SetJobOrderType(JobOrderType.OpenLine, "planner");
        order.JobOrderType.Should().Be(JobOrderType.OpenLine);
        order.ModifiedBy.Should().Be("planner", "the automatic audit interceptor records who changed it and when");
    }

    [Fact]
    public void TheLineType_StaysLockedOnACompletedOrder()
    {
        var order = OpenOrder();
        order.StageExecutions.Single().Skip(stageAllowsSkip: true, reason: "scrapped", modifiedBy: "tester");
        order.CompleteWithReason("closed with no output");

        var act = () => order.SetJobOrderType(JobOrderType.OpenLine, "planner");

        act.Should().Throw<DocumentLockedException>("a completed order's history is frozen");
    }

    [Fact]
    public void UpdateDetails_OnACompletedOrder_IsRefused()
    {
        var order = OpenOrder();
        order.StageExecutions.Single().Skip(stageAllowsSkip: true, reason: "scrapped", modifiedBy: "tester");
        order.CompleteWithReason("closed with no output");

        var act = () => order.UpdateDetails("Red", null, null, null, ProductionPriority.Low, DateTime.UtcNow, "planner");

        act.Should().Throw<DocumentLockedException>();
        order.Color.Should().Be("Navy", "a refused edit changes nothing");
    }

    [Fact]
    public void UpdateDetails_OnACancelledOrder_IsRefused()
    {
        var order = OpenOrder();
        order.Cancel("customer withdrew", "tester");

        var act = () => order.UpdateDetails("Red", null, null, null, ProductionPriority.Low, DateTime.UtcNow, "planner");

        act.Should().Throw<DocumentLockedException>();
        order.Color.Should().Be("Navy");
    }
}
