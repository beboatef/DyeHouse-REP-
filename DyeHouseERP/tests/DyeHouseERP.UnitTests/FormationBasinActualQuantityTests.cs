using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>Minimal current-user fake shared by the formation-basin handler tests.</summary>
internal sealed class StubCurrentUser : ICurrentUserService
{
    public StubCurrentUser(string userName) => UserName = userName;
    public string UserName { get; }
    public Guid? UserId => null;
    public string? IpAddress => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string permission) => true;
}

/// <summary>
/// Spec sections 8 and 10: a basin can be weighed for real while its request is
/// approved. A Formation Request is an independent document, so this weighing is
/// the request's own planning data and never creates or drives a Job Order.
/// </summary>
public class FormationBasinActualQuantityTests
{
    private static FormationRequest NewRequest() => new(
        "FRM-2026-000201", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
        UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid());

    private static (FormationRequest Request, FormationGroup Group) OneBasinGroup(decimal plannedKg)
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", plannedKg, UnitOfMeasure.KG, 1, "Navy", null, "tester");
        request.AddBasin(group.Id, "Basin 1", plannedKg, UnitOfMeasure.KG, 1, "Navy", null, "tester");

        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null,
            request.Groups.Sum(g => g.PlannedQuantity), "tester");
        request.Submit("tester");
        request.Approve("tester");

        return (request, group);
    }

    // ------------------------------------------------------------- domain

    [Fact]
    public void ABasinHasNoActualWeightUntilItIsWeighed_AndRemainingUsesThePlannedFigure()
    {
        var (_, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();

        basin.ActualQuantity.Should().BeNull();
        basin.EffectiveQuantity.Should().Be(500m, "before weighing, the planned figure is all there is");
        basin.RemainingQuantity.Should().Be(500m);
    }

    [Fact]
    public void RecordingTheActualWeight_ChangesEffectiveAndRemainingToTheActual()
    {
        var (request, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();

        request.RecordBasinActualQuantity(group.Id, basin.Id, 480m, "weigher");

        basin.ActualQuantity.Should().Be(480m);
        basin.PlannedQuantity.Should().Be(500m, "the planned figure is retained for comparison");
        basin.EffectiveQuantity.Should().Be(480m);
        basin.RemainingQuantity.Should().Be(480m);
    }

    [Fact]
    public void RemainingIsMeasuredAgainstTheActualWeight_SoALighterBasinCannotOverClaim()
    {
        var (request, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();
        request.RecordBasinActualQuantity(group.Id, basin.Id, 480m, "weigher");

        request.RecordBasinProduction(group.Id, basin.Id, 480m, "tester");

        // 480 actual - 480 produced = 0 outstanding. Against the PLANNED 500 the
        // basin would still have looked 20 KG outstanding that does not exist.
        basin.RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public void RecordingANonPositiveActualWeight_IsRefused()
    {
        var (request, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();

        var zero = () => request.RecordBasinActualQuantity(group.Id, basin.Id, 0m, "weigher");
        var negative = () => request.RecordBasinActualQuantity(group.Id, basin.Id, -5m, "weigher");

        zero.Should().Throw<ArgumentException>();
        negative.Should().Throw<ArgumentException>();
        basin.ActualQuantity.Should().BeNull();
    }

    [Fact]
    public void TheActualWeightCannotBePushedBelowWhatWasAlreadyProduced()
    {
        var (request, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();
        request.RecordBasinActualQuantity(group.Id, basin.Id, 600m, "weigher");
        request.RecordBasinProduction(group.Id, basin.Id, 550m, "tester");

        var act = () => request.RecordBasinActualQuantity(group.Id, basin.Id, 500m, "weigher");

        act.Should().Throw<ArgumentException>("the actual cannot be lowered under the quantity already booked");
        basin.ActualQuantity.Should().Be(600m);
    }

    [Fact]
    public void TheActualWeightCannotBeRecordedBeforeTheRequestIsApproved()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 500m, UnitOfMeasure.KG, 1, null, null, "tester");
        var basin = request.AddBasin(group.Id, "Basin 1", 500m, UnitOfMeasure.KG, 1, null, null, "tester");

        var act = () => request.RecordBasinActualQuantity(group.Id, basin.Id, 480m, "weigher");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TheGroupsPlannedFigureStaysPlannedBased_OnlyTheBasinCarriesTheActual()
    {
        var (request, group) = OneBasinGroup(500m);
        var basin = group.Basins.Single();

        request.RecordBasinActualQuantity(group.Id, basin.Id, 480m, "weigher");

        // The group's planned quantity is a planning figure and is not silently
        // rewritten by a weigh-in; the basin holds the production-truth figure.
        group.PlannedQuantity.Should().Be(500m);
        basin.EffectiveQuantity.Should().Be(480m);
    }

    [Fact]
    public void NoKgToMeterConversion_AcrossABasin()
    {
        var request = NewRequest();
        var group = request.AddGroup("Cell A", 2500m, UnitOfMeasure.Meter, 1, null, null, "tester");
        var basin = request.AddBasin(group.Id, "Basin Meter", 2500m, UnitOfMeasure.Meter, 1, null, null, "tester");
        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null, 2500m, "tester");
        request.Submit("tester");
        request.Approve("tester");

        request.RecordBasinActualQuantity(group.Id, basin.Id, 2400m, "weigher");

        basin.Unit.Should().Be(UnitOfMeasure.Meter);
        basin.ActualQuantity.Should().Be(2400m);
        basin.EffectiveQuantity.Should().Be(2400m, "a meter basin stays in meters - never converted to KG");
    }
}
