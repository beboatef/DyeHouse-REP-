using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 32-33 (customer returns to the raw material warehouse) and 12/54
/// (التشكيل is an explicit flag, never "whichever stage is Sequence 1").
/// </summary>
public class CustomerReturnTests
{
    private static CustomerReturn CreateReturn(string? reason = null)
        => new("CRT-2026-000001", DateTime.UtcNow, Guid.NewGuid(), "tester", reason);

    [Fact]
    public void Constructor_AllowsNoReasonBecauseTheReasonIsOptional()
    {
        var ret = CreateReturn(reason: null);

        ret.Reason.Should().BeNull();
        ret.Lines.Should().BeEmpty();
    }

    [Fact]
    public void AddLine_WithLotKgItem_Succeeds()
    {
        var ret = CreateReturn();
        var lotId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var line = ret.AddLine(lotId, itemId, quantityKg: 300m, quantityMeter: null);

        line.RawMessageId.Should().Be(lotId);
        line.ItemId.Should().Be(itemId);
        line.QuantityKg.Should().Be(300m);
        line.QuantityMeter.Should().BeNull();
        ret.Lines.Should().ContainSingle();
    }

    [Fact]
    public void AddLine_WithoutARawLot_Throws()
    {
        var ret = CreateReturn();

        var act = () => ret.AddLine(Guid.Empty, Guid.NewGuid(), quantityKg: 300m, quantityMeter: null);

        act.Should().Throw<DomainException>()
            .WithMessage("*raw material lot*");
    }

    [Fact]
    public void AddLine_WithNoQuantity_Throws()
    {
        var ret = CreateReturn();

        var act = () => ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), null, null);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void AddLine_WithNonPositiveQuantity_Throws(decimal quantity)
    {
        var ret = CreateReturn();

        var act = () => ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), quantity, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddLine_KeepsJobOrderAndBasinTraceabilityWhenKnown()
    {
        var ret = CreateReturn();
        var orderId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var line = ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), 250m, null, orderId, groupId, "partial roll");

        line.ProductionOrderId.Should().Be(orderId);
        line.FormationGroupId.Should().Be(groupId);
        line.Notes.Should().Be("partial roll");
    }

    [Fact]
    public void AddLine_WithoutJobOrderIsStillValid()
    {
        var ret = CreateReturn();

        var line = ret.AddLine(Guid.NewGuid(), Guid.NewGuid(), null, 120m);

        line.ProductionOrderId.Should().BeNull();
        line.FormationGroupId.Should().BeNull();
        line.QuantityMeter.Should().Be(120m);
    }

    [Fact]
    public void Post_LocksTheDocument()
    {
        var ret = CreateReturn();

        ret.IsLocked.Should().BeFalse();
        ret.Post();
        ret.IsLocked.Should().BeTrue();
    }
}

public class CreateCustomerReturnCommandValidatorTests
{
    private static readonly CreateCustomerReturnCommandValidator Validator = new();

    private static CreateCustomerReturnCommand Command(params CustomerReturnLineInput[] lines)
        => new(Guid.NewGuid(), DateTime.UtcNow, null, null, lines.ToList());

    [Fact]
    public void LineWithoutLotAndWithoutJobOrder_IsRejected()
    {
        var result = Validator.Validate(Command(
            new CustomerReturnLineInput(null, Guid.NewGuid(), 100m, null)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("raw material lot explicitly when the Job Order is unknown"));
    }

    [Fact]
    public void LineWithLotOnly_IsAccepted()
    {
        var result = Validator.Validate(Command(
            new CustomerReturnLineInput(Guid.NewGuid(), Guid.NewGuid(), 100m, null)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LineWithJobOrderOnly_IsAcceptedBecauseTheLotCanBeDerived()
    {
        var result = Validator.Validate(Command(
            new CustomerReturnLineInput(null, Guid.NewGuid(), null, 80m, Guid.NewGuid())));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ReturnWithoutLines_IsRejected()
    {
        Validator.Validate(Command()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LineWithoutQuantity_IsRejected()
    {
        var result = Validator.Validate(Command(
            new CustomerReturnLineInput(Guid.NewGuid(), Guid.NewGuid(), null, null)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReasonIsOptional()
    {
        var command = new CreateCustomerReturnCommand(
            Guid.NewGuid(), DateTime.UtcNow, Reason: null, Notes: null,
            Lines: new List<CustomerReturnLineInput> { new(Guid.NewGuid(), Guid.NewGuid(), 10m, null) });

        Validator.Validate(command).IsValid.Should().BeTrue();
    }
}

public class FormationStageFlagTests
{
    private static ProductionStageDefinition CreateStage(bool isFormationStage = false) =>
        new("FORM", "التشكيل", 1, "tester", isFormationStage: isFormationStage);

    [Fact]
    public void Stage_IsNotAFormationStageByDefault()
    {
        CreateStage().IsFormationStage.Should().BeFalse();
    }

    [Fact]
    public void Constructor_CanMarkTheFormationStage()
    {
        CreateStage(isFormationStage: true).IsFormationStage.Should().BeTrue();
    }

    [Fact]
    public void SetFormationStage_TogglesTheFlagAndRecordsWhoChangedIt()
    {
        var stage = CreateStage();

        stage.SetFormationStage(true, "admin");

        stage.IsFormationStage.Should().BeTrue();
        stage.ModifiedBy.Should().Be("admin");

        stage.SetFormationStage(false, "admin");
        stage.IsFormationStage.Should().BeFalse();
    }
}
