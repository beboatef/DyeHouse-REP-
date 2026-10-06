using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 35: a Job Order's additional cost lines are hand-entered and may
/// include نقل / تعبئة / إصلاح. External processing (تشغيل خارجي) is deliberately
/// NOT a category, because that charge is already derived from the
/// RawExternalRelease rows - accepting it here as well would double-count it into
/// the actual cost.
/// </summary>
public class CostCategoryTests
{
    [Fact]
    public void CostCategory_ContainsTheSpecAdditionalCostLines()
    {
        var names = Enum.GetNames<CostCategory>();

        names.Should().Contain(new[]
        {
            nameof(CostCategory.Labor),
            nameof(CostCategory.Electricity),
            nameof(CostCategory.Fuel),
            nameof(CostCategory.Maintenance),
            nameof(CostCategory.Transport),
            nameof(CostCategory.Packaging),
            nameof(CostCategory.Repair),
            nameof(CostCategory.Other)
        });
    }

    [Fact]
    public void CostCategory_DoesNotOfferExternalProcessingBecauseItIsDerived()
    {
        // Re-adding it as a hand-entered category would let the same outside charge
        // be counted twice in GetProductionOrderCostQuery.
        Enum.GetNames<CostCategory>().Should().NotContain("ExternalProcessing");
    }

    [Fact]
    public void ExistingCategoryValuesAreUnchangedSoStoredRowsStillResolve()
    {
        // The column is stored as a string, so the NAMES must stay exactly as they
        // are for every already-posted CostEntry row to keep resolving.
        ((int)CostCategory.Labor).Should().Be(1);
        ((int)CostCategory.Electricity).Should().Be(2);
        ((int)CostCategory.Fuel).Should().Be(3);
        ((int)CostCategory.Maintenance).Should().Be(4);
        ((int)CostCategory.Other).Should().Be(5);
        ((int)CostCategory.Transport).Should().Be(6);
        ((int)CostCategory.Packaging).Should().Be(7);
        ((int)CostCategory.Repair).Should().Be(8);
    }

    [Theory]
    [InlineData(CostCategory.Transport)]
    [InlineData(CostCategory.Packaging)]
    [InlineData(CostCategory.Repair)]
    public void CostEntry_AcceptsEachNewAdditionalCostLine(CostCategory category)
    {
        var orderId = Guid.NewGuid();

        var entry = new CostEntry(orderId, category, 1250.75m, DateTime.UtcNow, "tester", "spec section 35");

        entry.ProductionOrderId.Should().Be(orderId);
        entry.Category.Should().Be(category);
        entry.Amount.Should().Be(1250.75m);
        entry.Description.Should().Be("spec section 35");
    }

    [Fact]
    public void CostEntry_StillRejectsANegativeAmount()
    {
        var act = () => new CostEntry(Guid.NewGuid(), CostCategory.Transport, -1m, DateTime.UtcNow, "tester");

        act.Should().Throw<ArgumentException>();
    }
}
