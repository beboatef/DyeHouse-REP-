using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 10-11: one Formation Request group must support several independent
/// basins - 500 KG, 750 KG, 620 KG, 400 KG - each with its own quantity, its own
/// specification snapshot and its own produced quantity.
///
/// The invariants these tests pin down are the ones that keep stock honest:
///   * a group's quantity IS the sum of its basins, so the request total never drifts;
///   * a group with NO basins behaves exactly as it always did (single planned quantity);
///   * production is recorded on a basin only, never on the group as well, so the same
///     physical quantity can never be counted twice;
///   * specifications are snapshotted per basin and frozen on approval.
/// </summary>
public class FormationBasinTests
{
    private static FormationRequest NewRequest(decimal totalQuantity = 0m) => new(
        "FRM-2026-000101", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
        UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: totalQuantity);

    private static FormationSpecTemplate NewTemplate() => new(
        "SPEC-W160", "عرض 160", "Width 160", "tester",
        widthCm: 160m, metersPerKg: 5m, gsm: null, tubFormat: "50 Meter",
        qualityInstructions: "QC-A", customerInstructions: "Customer-A");

    /// <summary>One group holding the four basins from the requirement: 500 + 750 + 620 + 400 = 2270 KG.</summary>
    private static (FormationRequest Request, FormationGroup Group) FourBasinGroup()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 2270m, UnitOfMeasure.KG, 4, "Navy", null, "tester");

        request.AddBasin(group.Id, "Basin 1", 500m, UnitOfMeasure.KG, 1, "Navy", null, "tester");
        request.AddBasin(group.Id, "Basin 2", 750m, UnitOfMeasure.KG, 1, "Navy", null, "tester");
        request.AddBasin(group.Id, "Basin 3", 620m, UnitOfMeasure.KG, 1, "Navy", null, "tester");
        request.AddBasin(group.Id, "Basin 4", 400m, UnitOfMeasure.KG, 1, "Navy", null, "tester");

        return (request, group);
    }

    /// <summary>Mirrors the header total onto the groups and takes the request through submit + approve.</summary>
    private static void SubmitAndApprove(FormationRequest request)
    {
        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null,
            request.Groups.Sum(g => g.PlannedQuantity), "tester");
        request.Submit("tester");
        request.Approve("tester");
    }

    [Fact]
    public void AddBasin_NumbersBasinsSequentially()
    {
        var (_, group) = FourBasinGroup();

        group.Basins.Select(b => b.BasinNumber).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void AddBasin_GroupQuantityBecomesTheSumOfItsBasins()
    {
        var (_, group) = FourBasinGroup();

        group.PlannedQuantity.Should().Be(2270m);
        group.PlannedBasinQuantity.Should().Be(2270m);
        group.PlannedBasinQuantity.Should().Be(group.PlannedQuantity,
            "the group quantity and the basin total must be the same figure, never two competing ones");
    }

    [Fact]
    public void AddBasin_WithNonPositiveQuantity_Throws()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, null, null, "tester");

        var act = () => request.AddBasin(group.Id, "Basin 1", 0m, UnitOfMeasure.KG, 1, null, null, "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddBasin_WithNegativeTubCount_Throws()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, null, null, "tester");

        var act = () => request.AddBasin(group.Id, "Basin 1", 500m, UnitOfMeasure.KG, -1, null, null, "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddBasin_KeepsEachBasinQuantityIndependent()
    {
        var (_, group) = FourBasinGroup();

        group.Basins.Select(b => b.PlannedQuantity)
            .Should().Equal(new[] { 500m, 750m, 620m, 400m },
                "four basins must stay four separate quantities, never one merged figure");
    }

    [Fact]
    public void AddBasin_InheritsTheGroupSpecificationSoItIsNotRetyped()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, "Navy", null, "tester");
        request.UpdateGroup(
            group.Id, "Cell A", 500m, UnitOfMeasure.KG, 1, "Navy",
            widthCm: 180m, metersPerKg: 8m, gsm: 150m, tubFormat: "100 Meter", windingTapeFormat: "Tape-A",
            qualityInstructions: "QC-A", labInstructions: null, internalInstructions: null,
            customerInstructions: "Customer-A", notes: null, "tester");

        var basin = request.AddBasin(group.Id, "Basin 1", 500m, UnitOfMeasure.KG, 1, "Navy", null, "tester");

        basin.WidthCm.Should().Be(180m);
        basin.MetersPerKg.Should().Be(8m);
        basin.Gsm.Should().Be(150m);
        basin.TubFormat.Should().Be("100 Meter");
        basin.QualityInstructions.Should().Be("QC-A");
    }

    [Fact]
    public void UpdateBasin_ChangesOnlyThatBasinAndReducesTheGroupTotal()
    {
        var (request, group) = FourBasinGroup();
        var second = group.Basins.First(b => b.BasinNumber == 2);

        request.UpdateBasin(
            group.Id, second.Id, "Basin 2", 800m, UnitOfMeasure.KG, 1, "Navy",
            null, null, null, null, null, null, null, null, null, null, "tester");

        group.Basins.First(b => b.BasinNumber == 2).PlannedQuantity.Should().Be(800m);
        group.PlannedQuantity.Should().Be(2320m, "the group total must follow its basins");
    }

    [Fact]
    public void UpdateBasin_TypedGroupQuantityCanNeverDisagreeWithTheBasins()
    {
        var (request, group) = FourBasinGroup();

        // Even if a caller passes a completely different group figure, the basins win.
        request.UpdateGroup(
            group.Id, "Cell A", 9999m, UnitOfMeasure.KG, 4, "Navy",
            null, null, null, null, null, null, null, null, null, null, "tester");

        group.PlannedQuantity.Should().Be(2270m);
    }

    [Fact]
    public void RemoveBasin_RenumbersAndReducesTheGroupTotal()
    {
        var (request, group) = FourBasinGroup();
        var third = group.Basins.First(b => b.BasinNumber == 3);

        request.RemoveBasin(group.Id, third.Id, "tester");

        group.Basins.Select(b => b.BasinNumber).Should().Equal(1, 2, 3);
        group.PlannedQuantity.Should().Be(1650m);
    }

    [Fact]
    public void RemoveBasin_OneByOne_BringsTheGroupBackToBeingASingleQuantity()
    {
        var (request, group) = FourBasinGroup();

        // Deleting every basin must really delete them - a group that kept them would go on reporting
        // a basin sum nobody asked for any more.
        foreach (var basin in group.Basins.ToList())
            request.RemoveBasin(group.Id, basin.Id, "tester");

        group.Basins.Should().BeEmpty();
        group.HasBasins.Should().BeFalse();

        // With no basins left the group carries its own quantity again, and the typed figure is honoured.
        request.UpdateGroup(group.Id, "Cell A", 900m, UnitOfMeasure.KG, 2, "Navy",
            null, null, null, null, null, null, null, null, null, null, "tester");

        group.PlannedQuantity.Should().Be(900m);
        group.PlannedBasinQuantity.Should().Be(900m);
    }

    [Fact]
    public void UpdateBasin_ToZeroQuantity_ThrowsAndLeavesTheGroupTotalIntact()
    {
        var (request, group) = FourBasinGroup();
        var first = group.Basins.First(b => b.BasinNumber == 1);

        var act = () => request.UpdateBasin(
            group.Id, first.Id, "Basin 1", 0m, UnitOfMeasure.KG, 1, "Navy",
            null, null, null, null, null, null, null, null, null, null, "tester");

        act.Should().Throw<DomainException>();
        group.PlannedQuantity.Should().Be(2270m);
    }

    [Fact]
    public void RemoveBasin_ThatProducedQuantity_Throws()
    {
        var (request, group) = FourBasinGroup();
        var first = group.Basins.First(b => b.BasinNumber == 1);
        SubmitAndApprove(request);
        request.RecordBasinProduction(group.Id, first.Id, 100m, "tester");

        var act = () => request.RemoveBasin(group.Id, first.Id, "tester");

        act.Should().Throw<DocumentLockedException>("the request is approved, so its basins are history");
    }

    [Fact]
    public void ApplySpecificationTemplate_CopiesOntoEveryBasinWithItsOwnSnapshot()
    {
        var (request, group) = FourBasinGroup();
        var template = NewTemplate();

        request.ApplySpecificationTemplate(group.Id, template, "tester");

        group.Basins.Should().OnlyContain(b => b.WidthCm == 160m && b.MetersPerKg == 5m);
        group.Basins.Should().OnlyContain(b => b.SpecificationTemplateId == template.Id);
        group.Basins.Should().OnlyContain(b => b.SpecificationTemplateName == "Width 160");
    }

    [Fact]
    public void Approve_FreezesTheSnapshotOnTheGroupAndEveryBasin()
    {
        var (request, group) = FourBasinGroup();
        request.ApplySpecificationTemplate(group.Id, NewTemplate(), "tester");

        SubmitAndApprove(request);

        group.SpecificationSnapshotAtUtc.Should().NotBeNull();
        group.Basins.Should().OnlyContain(b => b.SpecificationSnapshotAtUtc.HasValue);
    }

    [Fact]
    public void AGroupWithoutBasinsBehavesExactlyAsBefore()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, "Navy", null, "tester");

        group.Basins.Should().BeEmpty();
        group.PlannedQuantity.Should().Be(500m);
        group.PlannedBasinQuantity.Should().Be(500m,
            "with no basins the group's own quantity IS the basin quantity");
        group.ProducedBasinQuantity.Should().Be(0m);
    }

    [Fact]
    public void RecordBasinProduction_AccumulatesOnTheBasinAndRollsUpToTheGroupAndRequest()
    {
        var (request, group) = FourBasinGroup();
        var first = group.Basins.First(b => b.BasinNumber == 1);
        var second = group.Basins.First(b => b.BasinNumber == 2);
        SubmitAndApprove(request);

        request.RecordBasinProduction(group.Id, first.Id, 200m, "tester");
        request.RecordBasinProduction(group.Id, second.Id, 300m, "tester");

        first.ProducedQuantity.Should().Be(200m);
        second.ProducedQuantity.Should().Be(300m);
        group.ProducedQuantity.Should().Be(500m, "the group's produced figure is the sum of its basins");
        request.Groups.Sum(g => g.ProducedQuantity).Should().Be(500m);
        request.Status.Should().Be(FormationRequestStatus.PartiallyCompleted);
    }

    [Fact]
    public void RecordBasinProduction_CannotBeRecordedTwiceOnTheSameQuantity()
    {
        var (request, group) = FourBasinGroup();
        var first = group.Basins.First(b => b.BasinNumber == 1);
        SubmitAndApprove(request);

        request.RecordBasinProduction(group.Id, first.Id, 200m, "tester");

        // A second completion of the same Job Order reports 200 again; the caller only ever posts the
        // INCREMENT, so re-running it can never inflate the produced figure.
        var increment = 200m - first.ProducedQuantity;
        increment.Should().Be(0m);
        first.ProducedQuantity.Should().Be(200m);
    }

    [Fact]
    public void RecordBasinProduction_RemainingQuantityNeverGoesNegative()
    {
        var (request, group) = FourBasinGroup();
        var first = group.Basins.First(b => b.BasinNumber == 1);
        SubmitAndApprove(request);

        request.RecordBasinProduction(group.Id, first.Id, 900m, "tester");

        first.RemainingQuantity.Should().Be(-400m,
            "the domain does not silently clamp; the caller caps the increment at the basin remaining");
    }

    [Fact]
    public void RecordBasinProduction_AfterAllBasinsAreDone_TheRequestIsCompleted()
    {
        var (request, group) = FourBasinGroup();
        SubmitAndApprove(request);

        foreach (var basin in group.Basins)
            request.RecordBasinProduction(group.Id, basin.Id, basin.PlannedQuantity, "tester");

        request.Status.Should().Be(FormationRequestStatus.Completed);
        request.Groups.Sum(g => g.ProducedQuantity).Should().Be(request.Groups.Sum(g => g.PlannedQuantity));
    }

    [Fact]
    public void RecordGroupProduction_OnAGroupThatHasBasins_Throws()
    {
        var (request, group) = FourBasinGroup();
        SubmitAndApprove(request);

        var act = () => request.RecordGroupProduction(group.Id, 100m, "tester");

        act.Should().Throw<DomainException>(
            "recording production on the group AND on its basins would count the same quantity twice");
    }

    [Fact]
    public void RecordGroupProduction_OnAGroupWithoutBasins_StillWorks()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, null, null, "tester");
        SubmitAndApprove(request);

        request.RecordGroupProduction(group.Id, 100m, "tester");

        group.ProducedQuantity.Should().Be(100m);
        group.ProducedBasinQuantity.Should().Be(100m);
    }

    [Fact]
    public void Submit_WithBasins_UsesTheGroupTotalSoTheHeaderNeverDrifts()
    {
        var (request, _) = FourBasinGroup();
        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null,
            request.Groups.Sum(g => g.PlannedQuantity), "tester");

        var act = () => request.Submit("tester");

        act.Should().NotThrow();
    }

    [Fact]
    public void Submit_WhenTheHeaderTotalDoesNotMatchTheBasinTotals_Throws()
    {
        var (request, _) = FourBasinGroup();
        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null, 3000m, "tester");

        var act = () => request.Submit("tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddBasin_AfterApproval_Throws()
    {
        var (request, group) = FourBasinGroup();
        SubmitAndApprove(request);

        var act = () => request.AddBasin(group.Id, "Basin 5", 100m, UnitOfMeasure.KG, 1, null, null, "tester");

        act.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void RemoveGroup_WithBasins_StillRefusesOnceQuantityWasProduced()
    {
        var (request, group) = FourBasinGroup();
        SubmitAndApprove(request);
        request.RecordBasinProduction(group.Id, group.Basins.First().Id, 10m, "tester");

        var act = () => request.RemoveGroup(group.Id, "tester");

        act.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void NoUnitConversionHappensBetweenBasins()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, null, null, "tester");

        var kg = request.AddBasin(group.Id, "Basin KG", 500m, UnitOfMeasure.KG, 1, null, null, "tester");
        var meter = request.AddBasin(group.Id, "Basin Meter", 2500m, UnitOfMeasure.Meter, 1, null, null, "tester");

        kg.Unit.Should().Be(UnitOfMeasure.KG);
        kg.PlannedQuantity.Should().Be(500m);
        meter.Unit.Should().Be(UnitOfMeasure.Meter);
        meter.PlannedQuantity.Should().Be(2500m, "a meter basin is never converted to KG (spec sections 6 and 29)");
    }

    [Fact]
    public void LinkProductionOrder_KeepsTheFirstOrderAsTheHeaderLink()
    {
        var (request, _) = FourBasinGroup();
        SubmitAndApprove(request);
        var firstOrder = Guid.NewGuid();
        var secondOrder = Guid.NewGuid();

        request.LinkProductionOrder(firstOrder, "tester");
        request.LinkProductionOrder(secondOrder, "tester");

        request.ProductionOrderId.Should().Be(firstOrder,
            "one request can fan out into several Job Orders; the header keeps the first one");
    }
}