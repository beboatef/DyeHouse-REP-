using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 30: an APPROVED delivery may still be corrected, but only through
/// compensating movements and never by rewriting history. These cover the rules
/// that guard that, at the level a compiler cannot check.
/// </summary>
public class DeliveryCorrectionTests
{
    private static Delivery DeliveredDelivery(out DeliveryLine line)
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var delivery = new Delivery("DEL-2026-0001", DateTime.UtcNow, Guid.NewGuid(), "tester");
        line = delivery.AddLine(orderId, itemId, "navy", 300m, null, null, null, null);
        delivery.MarkPrepared();
        delivery.MarkDelivered();
        return delivery;
    }

    [Fact]
    public void ApprovedLine_CanBeCorrectedToANewQuantity()
    {
        var delivery = DeliveredDelivery(out var line);
        line.QuantityKg.Should().Be(300m);

        line.AdjustQuantitiesAfterApproval(250m, null);

        line.QuantityKg.Should().Be(250m);
    }

    [Fact]
    public void ApprovedLine_RejectsGoingCompletelyBlank()
    {
        var delivery = DeliveredDelivery(out var line);

        var act = () => line.AdjustQuantitiesAfterApproval(null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ApprovedLine_RejectsANegativeCorrection()
    {
        var delivery = DeliveredDelivery(out var line);

        var act = () => line.AdjustQuantitiesAfterApproval(-5m, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EditAfterApproval_RequiresAReason()
    {
        var delivery = DeliveredDelivery(out _);

        var act = () => delivery.EditAfterApproval("   ", "manager");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EditAfterApproval_RecordsWhoAndWhyOnTheDocument()
    {
        var delivery = DeliveredDelivery(out _);

        delivery.EditAfterApproval("customer delivered 250 not 300", "manager");

        delivery.Notes.Should().Contain("250 not 300");
        delivery.ModifiedBy.Should().Be("manager");
    }

    [Fact]
    public void EditAfterApproval_IsRefusedOnADraftDelivery()
    {
        var delivery = new Delivery("DEL-2026-0002", DateTime.UtcNow, Guid.NewGuid(), "tester");

        var act = () => delivery.EditAfterApproval("too early", "manager");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddingALineIsStillRefusedAfterApproval()
    {
        var delivery = DeliveredDelivery(out _);

        // An approved delivery can be corrected, but not restructured.
        var act = () => delivery.AddLine(Guid.NewGuid(), Guid.NewGuid(), "red", 10m, null, null, null, null);

        act.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void PostApprovalEditIsItsOwnPermissionNotTheDeliverRight()
    {
        // Correcting a delivery the customer has already been charged against is a
        // sensitive action, so it must not ride on the ordinary delivery right.
        Permissions.All.Should().Contain(Permissions.ReadyEditPostApproval);
        Permissions.ReadyEditPostApproval.Should().NotBe(Permissions.ReadyDeliver);
        Permissions.All.Should().OnlyHaveUniqueItems();
    }
}
