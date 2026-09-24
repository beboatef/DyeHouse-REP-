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
    public void IsAvailableForAllocation_IsFalse_UntilInspectionAccepted()
    {
        var message = CreateMessage();

        message.IsAvailableForAllocation.Should().BeFalse("a message pending inspection must not be allocatable (spec section 11)");

        message.RecordInspection(InspectionStatus.Accepted, "inspector", null, DateTime.UtcNow);

        message.IsAvailableForAllocation.Should().BeTrue();
    }

    [Fact]
    public void IsAvailableForAllocation_IsFalse_WhenRejected()
    {
        var message = CreateMessage();

        message.RecordInspection(InspectionStatus.Rejected, "inspector", "damaged", DateTime.UtcNow);

        message.IsAvailableForAllocation.Should().BeFalse("rejected raw material must never become available production stock (spec section 11)");
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
