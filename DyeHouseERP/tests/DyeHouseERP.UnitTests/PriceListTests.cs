using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 34: the actual cost list and the customer service price list are
/// separate concepts, and a Job Order's applied price is a SNAPSHOT - later edits to
/// the list never change an order that already priced.
/// </summary>
public class StageCostRateTests
{
    [Fact]
    public void Rate_StoresStageUnitAndCost()
    {
        var stageId = Guid.NewGuid();

        var rate = new StageCostRate(stageId, UnitOfMeasure.KG, 12.5m, "tester", "dyeing cost");

        rate.StageDefinitionId.Should().Be(stageId);
        rate.Unit.Should().Be(UnitOfMeasure.KG);
        rate.CostPerUnit.Should().Be(12.5m);
        rate.IsActive.Should().BeTrue();
        rate.Notes.Should().Be("dyeing cost");
    }

    [Fact]
    public void Rate_RejectsANegativeCost()
    {
        var act = () => new StageCostRate(Guid.NewGuid(), UnitOfMeasure.KG, -1m, "tester");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Rate_RequiresAStage()
    {
        var act = () => new StageCostRate(Guid.Empty, UnitOfMeasure.KG, 5m, "tester");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Rate_UpdateAndDeactivateAreAudited()
    {
        var rate = new StageCostRate(Guid.NewGuid(), UnitOfMeasure.Meter, 3m, "tester");

        rate.Update(4.75m, null, "manager");
        rate.CostPerUnit.Should().Be(4.75m);
        rate.ModifiedBy.Should().Be("manager");

        rate.Deactivate("manager");
        rate.IsActive.Should().BeFalse();
        // The row survives deactivation - a rate that priced an order must stay resolvable.
        rate.CostPerUnit.Should().Be(4.75m);

        rate.Activate("manager");
        rate.IsActive.Should().BeTrue();
    }
}

public class CustomerServicePriceTests
{
    [Fact]
    public void Price_WithNoCustomerIsTheGeneralDefault()
    {
        var price = new CustomerServicePrice(Guid.NewGuid(), null, UnitOfMeasure.KG, 20m, "tester");

        price.IsGeneralDefault.Should().BeTrue();
        price.CustomerId.Should().BeNull();
    }

    [Fact]
    public void Price_WithACustomerIsACustomerOverride()
    {
        var customerId = Guid.NewGuid();

        var price = new CustomerServicePrice(Guid.NewGuid(), customerId, UnitOfMeasure.KG, 18m, "tester");

        price.IsGeneralDefault.Should().BeFalse();
        price.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public void Price_RejectsANegativeAmount()
    {
        var act = () => new CustomerServicePrice(Guid.NewGuid(), null, UnitOfMeasure.KG, -0.01m, "tester");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Price_UpdateChangesTheAmountAndIsAudited()
    {
        var price = new CustomerServicePrice(Guid.NewGuid(), null, UnitOfMeasure.KG, 20m, "tester");

        price.Update(22.5m, "annual revision", "pricing");

        price.PricePerUnit.Should().Be(22.5m);
        price.Notes.Should().Be("annual revision");
        price.ModifiedBy.Should().Be("pricing");
    }
}

public class ProductionOrderServicePriceTests
{
    private static ProductionOrderServicePrice Create(bool isOverride = false, string? reason = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), UnitOfMeasure.KG, 20m, null, isOverride, reason, "pricing");

    [Fact]
    public void Snapshot_StoresThePriceAndItsSource()
    {
        var customerId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var snapshot = new ProductionOrderServicePrice(
            orderId, stageId, UnitOfMeasure.Meter, 8.4m, customerId, false, null, "pricing");

        snapshot.ProductionOrderId.Should().Be(orderId);
        snapshot.StageDefinitionId.Should().Be(stageId);
        snapshot.Unit.Should().Be(UnitOfMeasure.Meter);
        snapshot.PricePerUnit.Should().Be(8.4m);
        snapshot.SourceCustomerId.Should().Be(customerId);
        snapshot.IsOverride.Should().BeFalse();
        snapshot.PreviousPricePerUnit.Should().BeNull();
        snapshot.PricedBy.Should().Be("pricing");
    }

    [Fact]
    public void Snapshot_RequiresAReasonForAManualOverride()
    {
        var act = () => Create(isOverride: true, reason: null);
        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void Snapshot_RejectsANegativePrice()
    {
        var act = () => new ProductionOrderServicePrice(
            Guid.NewGuid(), Guid.NewGuid(), UnitOfMeasure.KG, -1m, null, false, null, "pricing");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reapply_KeepsTheSupersededPriceAndRecordsWhoChangedIt()
    {
        var snapshot = Create();
        var newStage = Guid.NewGuid();

        snapshot.Reapply(newStage, 26m, null, true, "negotiated rate", "manager");

        snapshot.PricePerUnit.Should().Be(26m);
        // The previous figure stays on the row so the change is visible without the audit log.
        snapshot.PreviousPricePerUnit.Should().Be(20m);
        snapshot.StageDefinitionId.Should().Be(newStage);
        snapshot.IsOverride.Should().BeTrue();
        snapshot.OverrideReason.Should().Be("negotiated rate");
        snapshot.PricedBy.Should().Be("manager");
    }

    [Fact]
    public void Reapply_AlsoRequiresAReasonForAnotherOverride()
    {
        var snapshot = Create();

        var act = () => snapshot.Reapply(Guid.NewGuid(), 30m, null, true, null, "manager");

        act.Should().Throw<DomainException>().WithMessage("*reason*");
        // The refused call must not have half-applied anything.
        snapshot.PricePerUnit.Should().Be(20m);
    }

    [Fact]
    public void Reapply_FromTheListNeedsNoReason()
    {
        var snapshot = Create();
        var customerId = Guid.NewGuid();

        snapshot.Reapply(Guid.NewGuid(), 21m, customerId, false, null, "pricing");

        snapshot.PricePerUnit.Should().Be(21m);
        snapshot.SourceCustomerId.Should().Be(customerId);
        snapshot.IsOverride.Should().BeFalse();
    }
}
