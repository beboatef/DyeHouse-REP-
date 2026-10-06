using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 17 and 19. Two rules the factory depends on:
///   * الجاهز is an EXPLICIT marker, never inferred from Sequence, and can never be
///     the same stage as التشكيل - otherwise a Job Order would start and finish at
///     the same stop.
///   * Paused (موقوف مؤقتًا) is reversible; cancelled is not. Only one of the two
///     can ever be reached from an in-production order.
/// </summary>
public class ReadyGoodsStageMarkerTests
{
    [Fact]
    public void ReadyGoodsFlag_IsNotDerivedFromSequence()
    {
        // A stage at Sequence 1 is NOT automatically the ready-goods stage, and a
        // late-sequence stage is not automatically it either.
        var early = new ProductionStageDefinition("S1", "Formation", 1, "tester");
        var late = new ProductionStageDefinition("S9", "Ready", 99, "tester");

        early.IsReadyGoodsStage.Should().BeFalse();
        late.IsReadyGoodsStage.Should().BeFalse();
    }

    [Fact]
    public void ReadyGoodsStage_CanBeMarkedAndUnmarked()
    {
        var stage = new ProductionStageDefinition("S9", "Ready", 9, "tester");

        stage.SetReadyGoodsStage(true, "admin");
        stage.IsReadyGoodsStage.Should().BeTrue();
        stage.ModifiedBy.Should().Be("admin");

        stage.SetReadyGoodsStage(false, "admin");
        stage.IsReadyGoodsStage.Should().BeFalse();
    }

    [Fact]
    public void AStageCannotBeBothTheFirstAndTheLastStop()
    {
        var stage = new ProductionStageDefinition("S1", "Formation", 1, "tester", isFormationStage: true);

        var act = () => stage.SetReadyGoodsStage(true, "admin");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheFormationStageCannotLaterBecomeTheReadyGoodsStage()
    {
        var stage = new ProductionStageDefinition("S9", "Ready", 9, "tester", isReadyGoodsStage: true);

        var act = () => stage.SetFormationStage(true, "admin");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreatingAStageAsBothAtOnceIsRefused()
    {
        var act = () => new ProductionStageDefinition(
            "S1", "Formation", 1, "tester", isFormationStage: true, isReadyGoodsStage: true);

        act.Should().Throw<ArgumentException>();
    }
}

public class ProductionOrderPauseTests
{
    private static ProductionOrder InProductionOrder()
    {
        var order = new ProductionOrder(
            "PRO-2026-0001", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester",
            requestedQuantityKg: 800m);

        // Draft -> RawAllocated happens when raw material is allocated.
        order.AllocateRaw(Guid.NewGuid(), Guid.NewGuid(), 800m, null, "tester");
        order.MarkInProduction();
        return order;
    }

    [Fact]
    public void Pausing_KeepsTheOrderResumableAndIsDistinctFromCancelling()
    {
        var order = InProductionOrder();

        order.Pause("machine breakdown", "supervisor");

        order.Status.Should().Be(ProductionOrderStatus.Paused);
        // The order is still resumable: pause releases stock, it does not end the job.
        order.Status.Should().NotBe(ProductionOrderStatus.Cancelled);
        order.Notes.Should().Contain("machine breakdown");
    }

    [Fact]
    public void PausingTwiceIsRefused()
    {
        var order = InProductionOrder();
        order.Pause("first", "supervisor");

        var act = () => order.Pause("again", "supervisor");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ACompletedOrderCannotBePaused()
    {
        var order = InProductionOrder();
        // A completed order has left production for good - there is no unused raw
        // material left attached to it to release.
        order.Complete();
        order.Status.Should().Be(ProductionOrderStatus.Completed);

        var act = () => order.Pause("too late", "supervisor");

        act.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void ADraftOrderCannotBePausedBecauseNothingWasIssued()
    {
        var order = new ProductionOrder("PRO-2026-0002", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "tester");

        var act = () => order.Pause("nothing to release", "supervisor");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResumingRequiresAPausedOrder()
    {
        var order = InProductionOrder();

        var act = () => order.Resume("not paused", "supervisor");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResumingRequiresAReason()
    {
        var order = InProductionOrder();
        order.Pause("paused", "supervisor");

        var act = () => order.Resume("  ", "supervisor");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResumingReturnsTheSameOrderToProduction()
    {
        var order = InProductionOrder();
        var originalNumber = order.OrderNumber;
        order.Pause("machine breakdown", "supervisor");

        order.Resume("machine repaired", "supervisor");

        // The SAME order continues - no new number, no new document.
        order.OrderNumber.Should().Be(originalNumber);
        order.Status.Should().Be(ProductionOrderStatus.InProduction);
        order.Notes.Should().Contain("machine repaired");
    }

    [Fact]
    public void PausedStatusIsDistinctFromCancelled()
    {
        // Guards the concept separation itself: a paused order can come back, a
        // cancelled one cannot, so they must never collapse into the same state.
        ProductionOrderStatus.Paused.Should().NotBe(ProductionOrderStatus.Cancelled);
        ((int)ProductionOrderStatus.Paused).Should().NotBe((int)ProductionOrderStatus.Cancelled);
    }
}