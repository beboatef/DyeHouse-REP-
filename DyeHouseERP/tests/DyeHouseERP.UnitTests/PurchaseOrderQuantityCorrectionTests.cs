using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 35: an approved purchase order line that has already been received from can no longer be
/// freely edited - the received quantity and the stock movements it posted are real and must never be
/// rewritten. The outstanding (unreceived) balance is instead corrected through the dedicated, reason-
/// bearing, permission-guarded path, which can never take the ordered quantity below what already arrived.
/// </summary>
public class PurchaseOrderQuantityCorrectionTests
{
    private static PurchaseOrder NewApprovedOrder(decimal ordered, out Guid lineId)
    {
        var order = new PurchaseOrder("PO-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "buyer");
        var line = order.AddLine(Guid.NewGuid(), ordered, MaterialUnit.KG, 10m, null, "buyer");
        order.Submit("buyer");
        order.Approve("manager");
        lineId = line.Id;
        return order;
    }

    [Fact]
    public void ReduceToTheReceivedQuantity_ClosesTheOutstandingBalance()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 60m, MaterialUnit.KG, "storekeeper");
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);

        order.CorrectOrderedQuantityAfterReceipt(lineId, 60m, "supplier delivered short", "buyer");

        var line = order.Lines.Single();
        line.Quantity.Should().Be(60m);
        line.ReceivedQuantity.Should().Be(60m, "the correction never rewrites what arrived");
        line.OutstandingQuantity.Should().Be(0m);
        order.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void IncreaseTheOrderedQuantity_KeepsTheReceivedPartIntact()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 40m, MaterialUnit.KG, "storekeeper");

        order.CorrectOrderedQuantityAfterReceipt(lineId, 150m, "additional requirement", "buyer");

        var line = order.Lines.Single();
        line.Quantity.Should().Be(150m);
        line.ReceivedQuantity.Should().Be(40m);
        line.OutstandingQuantity.Should().Be(110m);
        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void ReducingBelowWhatWasReceived_IsRefused_AndChangesNothing()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 60m, MaterialUnit.KG, "storekeeper");

        var act = () => order.CorrectOrderedQuantityAfterReceipt(lineId, 50m, "typo", "buyer");

        act.Should().Throw<DomainException>().WithMessage("*cannot be lower*");
        order.Lines.Single().Quantity.Should().Be(100m);
    }

    [Fact]
    public void Reason_IsRequired()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 10m, MaterialUnit.KG, "storekeeper");

        var act = () => order.CorrectOrderedQuantityAfterReceipt(lineId, 90m, "   ", "buyer");
        act.Should().Throw<DomainException>().WithMessage("*requires a reason*");
    }

    [Fact]
    public void NonPositiveQuantity_IsRefused()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 10m, MaterialUnit.KG, "storekeeper");

        var act = () => order.CorrectOrderedQuantityAfterReceipt(lineId, 0m, "close", "buyer");
        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Correction_IsRecordedOnTheLine()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 10m, MaterialUnit.KG, "storekeeper");

        order.CorrectOrderedQuantityAfterReceipt(lineId, 80m, "supplier reduced the batch", "buyer");

        order.Lines.Single().Notes.Should().Contain("Ordered quantity corrected")
            .And.Contain("supplier reduced the batch");
    }

    [Fact]
    public void DraftOrSubmittedOrder_IsEditedTheOrdinaryWay_NotAsAReceiptCorrection()
    {
        var draft = new PurchaseOrder("PO-2", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), "buyer");
        var line = draft.AddLine(Guid.NewGuid(), 10m, MaterialUnit.KG, 5m, null, "buyer");

        var act = () => draft.CorrectOrderedQuantityAfterReceipt(line.Id, 8m, "before approval", "buyer");
        act.Should().Throw<DomainException>().WithMessage("*not been approved*");
    }

    [Fact]
    public void CancelledOrder_IsRefused()
    {
        var order = NewApprovedOrder(100m, out var lineId);
        order.RecordReceivedQuantity(lineId, 10m, MaterialUnit.KG, "storekeeper");
        order.Cancel("buyer", "no longer needed");

        var act = () => order.CorrectOrderedQuantityAfterReceipt(lineId, 90m, "close", "buyer");
        act.Should().Throw<DocumentLockedException>();
    }

    [Fact]
    public void Validator_EnforcesReasonAndPositiveQuantity()
    {
        new CorrectPurchaseOrderLineQuantityCommandValidator()
            .Validate(new CorrectPurchaseOrderLineQuantityCommand(Guid.NewGuid(), Guid.NewGuid(), 5m, ""))
            .IsValid.Should().BeFalse("a reason is mandatory");

        new CorrectPurchaseOrderLineQuantityCommandValidator()
            .Validate(new CorrectPurchaseOrderLineQuantityCommand(Guid.NewGuid(), Guid.NewGuid(), 0m, "why"))
            .IsValid.Should().BeFalse("a non-positive ordered quantity is refused at the boundary");

        new CorrectPurchaseOrderLineQuantityCommandValidator()
            .Validate(new CorrectPurchaseOrderLineQuantityCommand(Guid.NewGuid(), Guid.NewGuid(), 5m, "why"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void AfterReceiptCorrection_IsAConfigurablePermissionOfItsOwn()
    {
        Permissions.PurchasesEditAfterReceipt.Should().NotBe(Permissions.PurchasesEdit);
        Permissions.All.Should().Contain(Permissions.PurchasesEditAfterReceipt);
    }

    [Fact]
    public void OrderCarriesAConcurrencyToken()
    {
        typeof(PurchaseOrder).GetProperty("RowVersion").Should().NotBeNull(
            "a correction must not be computed against a stale received quantity");
    }
}
