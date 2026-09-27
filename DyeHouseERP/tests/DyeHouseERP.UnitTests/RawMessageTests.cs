using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class RawMessageTests
{
    private static RawMessage CreateMessage() =>
        new("MSG-2026-000001", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "tester", "tester");

    [Fact]
    public void AddLine_WithOnlyKg_Succeeds()
    {
        var message = CreateMessage();

        var line = message.AddLine(Guid.NewGuid(), quantityKg: 1000m, quantityMeter: null, pieceCount: 12, notes: null);

        line.QuantityKg.Should().Be(1000m);
        line.QuantityMeter.Should().BeNull();
    }

    [Fact]
    public void AddLine_WithNeitherKgNorMeter_Throws()
    {
        var message = CreateMessage();

        var act = () => message.AddLine(Guid.NewGuid(), quantityKg: null, quantityMeter: null, pieceCount: null, notes: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddLine_WithZeroQuantity_Throws()
    {
        var message = CreateMessage();

        var act = () => message.AddLine(Guid.NewGuid(), quantityKg: 0m, quantityMeter: null, pieceCount: null, notes: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void IsAvailableForAllocation_DoesNotRequireInspectionApproval()
    {
        var message = CreateMessage();

        // Spec section 9: inspection is recorded information only - there is NO
        // mandatory inspection approval step blocking a receipt from posting.
        message.IsAvailableForAllocation.Should().BeTrue(
            "a receipt must be allowed to post and be allocatable without inspection approval (spec section 9)");

        message.RecordInspection(InspectionStatus.Accepted, "inspector", null, DateTime.UtcNow);

        message.IsAvailableForAllocation.Should().BeTrue();
    }

    [Fact]
    public void RecordInspection_WithRejection_KeepsItTraceableButOutOfStock()
    {
        var message = CreateMessage();
        var line = message.AddLine(Guid.NewGuid(), quantityKg: 1000m, quantityMeter: null, pieceCount: null, notes: null);

        message.RecordInspection(InspectionStatus.AcceptedWithNotes, "inspector", "50 KG damaged", DateTime.UtcNow);
        var recorded = message.RecordLineRejection(line.Id, rejectedKg: 50m, rejectedMeter: null);

        recorded.Kg.Should().Be(50m, "the delta is what the caller posts to the ledger");
        message.HasRejections.Should().BeTrue();
        line.RejectedQuantityKg.Should().Be(50m);
        line.AcceptedQuantityKg.Should().Be(950m, "rejected material must never count as accepted stock (spec section 9)");
    }

    [Fact]
    public void RecordLineRejection_RejectingMoreThanReceived_Throws()
    {
        var message = CreateMessage();
        var line = message.AddLine(Guid.NewGuid(), quantityKg: 100m, quantityMeter: null, pieceCount: null, notes: null);

        var act = () => message.RecordLineRejection(line.Id, rejectedKg: 150m, rejectedMeter: null);

        act.Should().Throw<DomainException>("a rejected quantity can never exceed what was received (spec section 55)");
    }

    [Fact]
    public void RecordLineRejection_Accumulates_AndRefusesAUnitThatWasNeverReceived()
    {
        var message = CreateMessage();
        var line = message.AddLine(Guid.NewGuid(), quantityKg: 100m, quantityMeter: null, pieceCount: null, notes: null);

        message.RecordLineRejection(line.Id, rejectedKg: 10m, rejectedMeter: null);
        var second = message.RecordLineRejection(line.Id, rejectedKg: 5m, rejectedMeter: null);

        second.Kg.Should().Be(5m);
        line.RejectedQuantityKg.Should().Be(15m);

        var act = () => message.RecordLineRejection(line.Id, rejectedKg: null, rejectedMeter: 5m);
        act.Should().Throw<DomainException>("no Meter quantity was received on this line, so none can be rejected");
    }

    [Fact]
    public void RecordLineRejection_ForALineFromAnotherMessage_Throws()
    {
        var message = CreateMessage();
        message.AddLine(Guid.NewGuid(), quantityKg: 100m, quantityMeter: null, pieceCount: null, notes: null);

        var act = () => message.RecordLineRejection(Guid.NewGuid(), rejectedKg: 1m, rejectedMeter: null);

        act.Should().Throw<DomainException>();
    }
}

public class NegativeStockExceptionTests
{
    [Fact]
    public void Shortage_IsCalculated_AsRequestedMinusCurrent()
    {
        var ex = new NegativeStockException(currentBalance: 500m, requestedQuantity: 550m);

        ex.Shortage.Should().Be(50m);
    }
}
