using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.RawExternalReleases.Commands;
using DyeHouseERP.Application.ReadyGoods.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using ApprovalEntity = DyeHouseERP.Domain.Entities.RawExternalRelease;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Shared fixture for the cancellation hardening: an InMemory DbContext with
/// a customer, an item, a production order and a Ready Goods warehouse.
/// </summary>
internal sealed class CancellationFixture : IDisposable
{
    public Customer Customer { get; } = new("C-1", "Customer", "seed");
    public Item Item { get; } = new("IT-1", "Item", default(UnitOfMeasure), "seed");
    public Warehouse ReadyWarehouse { get; } = new("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
    public ProductionOrder Order { get; }
    public Persistence.ApplicationDbContext Db { get; }

    public CancellationFixture()
    {
        Order = new ProductionOrder("PRD-1", Customer.Id, Item.Id, DateTime.UtcNow, "seed");

        var options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"cancel-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new Persistence.ApplicationDbContext(options);
        Db.Customers.Add(Customer);
        Db.Items.Add(Item);
        Db.Warehouses.Add(ReadyWarehouse);
        Db.ProductionOrders.Add(Order);
        Db.SaveChanges();
    }

    public ReadyGoodsTransfer PostTransfer(decimal kg)
    {
        var transfer = new ReadyGoodsTransfer(
            $"RGT-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, Order.Id, Customer.Id, Item.Id,
            ReadyWarehouse.Id, kg, null, "seed");
        Db.ReadyGoodsTransfers.Add(transfer);
        Db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, DateTime.UtcNow,
            ReadyWarehouse.Id, Customer.Id, Item.Id,
            rawMessageId: null, productionOrderId: Order.Id,
            quantityKg: kg, quantityMeter: null,
            direction: TransactionDirection.In, createdBy: "seed"));
        Db.SaveChanges();
        return transfer;
    }

    public Delivery BuildPreparedDelivery(decimal kg)
    {
        var delivery = new Delivery($"DLV-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, Customer.Id, "seed");
        delivery.AddLine(Order.Id, Item.Id, null, kg, null, null, null, null);
        delivery.MarkPrepared();
        Db.Deliveries.Add(delivery);
        Db.SaveChanges();
        return delivery;
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>
/// Task 2 regression: the permission/delivery-lock guard must run INSIDE the
/// stock lock, so a transfer whose production order already has a Delivered
/// delivery cannot be cancelled - and the failed attempt posts nothing.
/// </summary>
public class DeleteReadyGoodsTransferGuardTests
{
    [Fact]
    public async Task Cannot_Cancel_Transfer_When_Production_Order_Already_Has_A_Delivered_Delivery()
    {
        using var fx = new CancellationFixture();
        var transfer = fx.PostTransfer(kg: 100m);

        var delivery = fx.BuildPreparedDelivery(kg: 100m);
        var deliverHandler = new MarkDeliveryDeliveredCommandHandler(
            fx.Db,
            Mock.Of<ICurrentUserService>(u => u.UserName == "operator"),
            new FixedClock(),
            new OpenPeriod(),
            new RecordingStockLock());
        await deliverHandler.Handle(new MarkDeliveryDeliveredCommand(delivery.Id), CancellationToken.None);

        var stockLock = new RecordingStockLock();
        var handler = new DeleteReadyGoodsTransferCommandHandler(
            fx.Db,
            new InventoryMovementPermissionService(
                Mock.Of<ICurrentUserService>(u => u.UserName == "admin" && u.HasPermission(Permissions.InventoryDelete)),
                fx.Db),
            Mock.Of<ICurrentUserService>(u => u.UserName == "admin"),
            new FixedClock(),
            new OpenPeriod(),
            stockLock);

        var act = async () => await handler.Handle(
            new DeleteReadyGoodsTransferCommand(transfer.Id, "should fail"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentLockedException>();
        ex.Which.Message.Should().Contain("delivered");

        // No reversal rows may exist for the transfer's IN row.
        var reversals = fx.Db.InventoryTransactions
            .Where(t => t.ReversesTransactionId != null)
            .ToList();
        reversals.Should().BeEmpty("a blocked cancellation must not post any ledger rows");
        fx.Db.InventoryTransactions.Count(t => t.SourceDocumentType == DocumentType.ReadyGoodsTransfer)
            .Should().Be(1, "only the original IN row remains");

        // The lock was still taken (guard runs inside the lock).
        stockLock.Dimensions.Should().ContainSingle(k => k.ItemId == fx.Item.Id && k.ProductionOrderId == fx.Order.Id);
    }
}

/// <summary>
/// Task 3 regression: cancelling an external-processing movement is a
/// workflow-state change ONLY - it must not post any ledger rows and must
/// not restore the customer's raw-material balance. A second cancellation
/// is rejected.
/// </summary>
public class CancelExternalProcessingHandlerTests
{
    private static (CancellationFixture Fx, RawMessage Message, ApprovalEntity Release) SeedRelease(
        CancellationFixture fx, decimal kg)
    {
        var message = new RawMessage(
            $"MSG-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, fx.Customer.Id, fx.ReadyWarehouse.Id,
            "receiver", "seed");
        // Attach a line via the internal collection through EF navigation.
        fx.Db.RawMessages.Add(message);
        fx.Db.SaveChanges();
        fx.Db.Entry(message).Collection(m => m.Lines).IsLoaded = true;

        var release = new ApprovalEntity(
            $"REL-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, fx.Customer.Id, fx.Item.Id, message.Id,
            kg, null, RawReleaseReason.ExternalProcessing, "seed",
            productionOrderId: fx.Order.Id, externalProcessingStage: "Dyeing");
        fx.Db.RawExternalReleases.Add(release);
        fx.Db.SaveChanges();
        return (fx, message, release);
    }

    private static CancelExternalProcessingCommandHandler NewHandler(
        CancellationFixture fx, RecordingStockLock stockLock) => new(
        fx.Db,
        Mock.Of<ICurrentUserService>(u => u.UserName == "planner"),
        stockLock);

    [Fact]
    public async Task Cancel_Posts_No_Ledger_Rows_And_Does_Not_Restore_Customer_Raw_Balance()
    {
        using var fx = new CancellationFixture();
        var (_, message, release) = SeedRelease(fx, kg: 80m);

        // Baseline: no ledger activity for this message.
        var before = fx.Db.InventoryTransactions.Count(t => t.RawMessageId == message.Id);

        await NewHandler(fx, new RecordingStockLock()).Handle(
            new CancelExternalProcessingCommand(release.Id, "wrong workflow"), CancellationToken.None);

        fx.Db.Entry(release).Reload();
        release.Status.Should().Be(ExternalProcessingStatus.Cancelled);

        var after = fx.Db.InventoryTransactions.Where(t => t.RawMessageId == message.Id).ToList();
        after.Should().HaveCount(before, "cancellation posts NO IN/OUT ledger rows");
        after.Where(t => t.Direction == TransactionDirection.In)
            .Should().BeEmpty("cancelling must never credit the raw lot back");
    }

    [Fact]
    public async Task Second_Cancel_Is_Rejected()
    {
        using var fx = new CancellationFixture();
        var (_, message, release) = SeedRelease(fx, kg: 50m);

        var handler = NewHandler(fx, new RecordingStockLock());
        await handler.Handle(new CancelExternalProcessingCommand(release.Id, "first"), CancellationToken.None);

        var act = async () => await handler.Handle(
            new CancelExternalProcessingCommand(release.Id, "second"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("already cancelled");

        fx.Db.Entry(release).Reload();
        release.Status.Should().Be(ExternalProcessingStatus.Cancelled);
    }

    [Fact]
    public async Task Missing_RawMessage_Throws_NotFound_Not_KeyOrArgument()
    {
        using var fx = new CancellationFixture();
        var release = new ApprovalEntity(
            $"REL-{Guid.NewGuid():N}"[..16], DateTime.UtcNow, fx.Customer.Id, fx.Item.Id, Guid.NewGuid(),
            40m, null, RawReleaseReason.ExternalProcessing, "seed",
            productionOrderId: fx.Order.Id, externalProcessingStage: "Dyeing");
        fx.Db.RawExternalReleases.Add(release);
        fx.Db.SaveChanges();

        var act = async () => await NewHandler(fx, new RecordingStockLock()).Handle(
            new CancelExternalProcessingCommand(release.Id, "orphan"), CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>())
            .Which.Message.Should().Contain("RawMessage");
    }
}
