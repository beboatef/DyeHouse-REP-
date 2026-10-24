using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class SeparatesDeliveryRuleTests
{
    [Fact]
    public void DeliveryPermit_RejectsSeparatesAsADeliverableCategory()
    {
        var command = new CreateDeliveryCommand(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "test",
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = Guid.NewGuid(),
                    ItemId = Guid.NewGuid(),
                    Category = ReadyGoodsCategory.Separates,
                    QuantityKg = 10m
                }
            });

        var result = new CreateDeliveryCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("cannot be delivered to the customer"));
    }

    [Theory]
    [InlineData(ReadyGoodsCategory.FirstGrade)]
    [InlineData(ReadyGoodsCategory.SecondGrade)]
    public void DeliveryPermit_AllowsCustomerDeliverableReadyGoodsCategories(ReadyGoodsCategory category)
    {
        var command = new CreateDeliveryCommand(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "test",
            new List<DeliveryLineInput>
            {
                new()
                {
                    ProductionOrderId = Guid.NewGuid(),
                    ItemId = Guid.NewGuid(),
                    Category = category,
                    QuantityKg = 10m
                }
            });

        var result = new CreateDeliveryCommandValidator().Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
