using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.RawReceipts.Commands;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Application.Treasury.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Shared fakes for the concurrency hardening. The real implementation is
/// sp_getapplock (SQL Server only), so handler tests substitute a recording
/// no-op and assert on WHICH dimensions the handler asked to lock - the part
/// that is pure logic and would otherwise be untested.
/// </summary>
internal sealed class RecordingStockLock : IAllocationLockService
{
    public List<StockLockKey> Dimensions { get; } = new();
    public List<string> NamedResources { get; } = new();

    public Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
    {
        Dimensions.Add(key);
        return Task.FromResult<IAsyncDisposable>(Noop.Instance);
    }

    public Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
    {
        Dimensions.AddRange(keys);
        return Task.FromResult<IAsyncDisposable>(Noop.Instance);
    }

    public Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
    {
        NamedResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(Noop.Instance);
    }

    public Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
    {
        NamedResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(Noop.Instance);
    }
    public Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
    {
        NamedResources.Add(resource);
        return Task.FromResult<IAsyncDisposable>(Noop.Instance);
    }

    private sealed class Noop : IAsyncDisposable
    {
        public static readonly Noop Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FixedClock : IDateTime
{
    public DateTime UtcNow { get; } = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
}

internal sealed class OpenPeriod : IPeriodCloseService
{
    public Task EnsureOpenAsync(DateTime date, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IAsyncDisposable> BeginPostingAsync(DateTime date, CancellationToken cancellationToken = default)
        => Task.FromResult<IAsyncDisposable>(NoopPeriodGuard.Instance);
}

/// <summary>Shared no-op guard used by every fake period service (unit tests).</summary>
internal sealed class NoopPeriodGuard : IAsyncDisposable
{
    public static readonly NoopPeriodGuard Instance = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FixedNumberGenerator : IDocumentNumberGenerator
{
    public Task<string> NextAsync(DocumentType documentType, Guid? warehouseId = null, CancellationToken cancellationToken = default)
        => Task.FromResult($"{documentType}-TEST-1");
}

/// <summary>
/// H6: the unified stock lock key. Two writers that touch the same stock
/// dimension must produce the SAME resource string (so they actually serialize),
/// and different dimensions must never collide (so unrelated stock does not
/// block unrelated stock).
/// </summary>
public class StockLockKeyTests
{
    [Fact]
    public void Same_Dimension_Always_Produces_The_Same_Resource()
    {
        var a = StockLockKey.RawLot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var b = StockLockKey.RawLot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Re-create the identical tuple and compare.
        var first = StockLockKey.RawLot(a.WarehouseId!.Value, a.ItemId!.Value, a.CustomerId!.Value, a.RawMessageId!.Value);
        var second = StockLockKey.RawLot(a.WarehouseId!.Value, a.ItemId!.Value, a.CustomerId!.Value, a.RawMessageId!.Value);

        first.Resource.Should().Be(second.Resource);
        a.Resource.Should().NotBe(b.Resource);
    }

    [Fact]
    public void Different_Dimensions_Never_Collide()
    {
        var warehouse = Guid.NewGuid();
        var item = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var message = Guid.NewGuid();

        var resources = new[]
        {
            StockLockKey.RawLot(warehouse, item, customer, message).Resource,
            StockLockKey.RawLot(warehouse, item, Guid.NewGuid(), message).Resource,
            StockLockKey.RawLot(warehouse, Guid.NewGuid(), customer, message).Resource,
            StockLockKey.RawLot(Guid.NewGuid(), item, customer, message).Resource,
            StockLockKey.RawLot(warehouse, item, customer, Guid.NewGuid()).Resource,
            StockLockKey.ReadyLot(item, Guid.NewGuid()).Resource,
            StockLockKey.ReadyLot(Guid.NewGuid(), Guid.NewGuid()).Resource,
            StockLockKey.MaterialLot(warehouse, item).Resource
        };

        resources.Distinct().Should().HaveCount(resources.Length);
    }

    [Fact]
    public void ReadyLot_Same_Item_And_Order_Is_The_Same_Key_Regardless_Of_Warehouse()
    {
        var item = Guid.NewGuid();
        var order = Guid.NewGuid();

        // Two paths resolving DIFFERENT ready warehouses (or one passing the
        // request's warehouse and one the lookup's) must still serialize.
        var viaWarehouseA = StockLockKey.ReadyLot(item, order);
        var viaWarehouseB = StockLockKey.ReadyLot(item, order);

        viaWarehouseA.Resource.Should().Be(viaWarehouseB.Resource);
        viaWarehouseA.WarehouseId.Should().BeNull();
        viaWarehouseA.CustomerId.Should().BeNull();

        // A different order (or item) is a different dimension.
        StockLockKey.ReadyLot(item, Guid.NewGuid()).Resource.Should().NotBe(viaWarehouseA.Resource);
        StockLockKey.ReadyLot(Guid.NewGuid(), order).Resource.Should().NotBe(viaWarehouseA.Resource);
    }

    [Fact]
    public void Unset_Dimensions_Are_Explicit_Rather_Than_Empty()
    {
        var key = new StockLockKey { WarehouseId = Guid.NewGuid() };
        key.Resource.Should().StartWith("Stock:").And.Contain("any");
    }
}

/// <summary>
/// H3: a delivery whose lines repeat the same (ProductionOrder, Item) must be
/// validated on the COMBINED quantity. Checking each duplicate line against the
/// full available balance separately let a delivery consume more ready stock
/// than exists.
/// </summary>
public class DeliveryDuplicateLineBalanceTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public DeliveryDuplicateLineBalanceTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"delivery-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    [Fact]
    public async Task Duplicate_Lines_Are_Validated_On_Their_Combined_Quantity()
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", default(UnitOfMeasure), "seed");
        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, DateTime.UtcNow, "seed");
        var warehouse = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");

        using var db = NewDb();
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.ProductionOrders.Add(order);
        db.Warehouses.Add(warehouse);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ReadyGoodsTransfer, "RGT-1", Guid.NewGuid(), DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id,
            rawMessageId: null, productionOrderId: order.Id,
            quantityKg: 100m, quantityMeter: null,
            direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        // Two lines of 60 KG each = 120 KG requested against 100 KG available.
        // Each line alone would have passed the old per-line check.
        var delivery = new Delivery("DLV-1", DateTime.UtcNow, customer.Id, "seed");
        delivery.AddLine(order.Id, item.Id, null, 60m, null, null, null, null);
        delivery.AddLine(order.Id, item.Id, null, 60m, null, null, null, null);
        delivery.MarkPrepared();
        db.Deliveries.Add(delivery);
        db.SaveChanges();

        var stockLock = new RecordingStockLock();
        var handler = new MarkDeliveryDeliveredCommandHandler(
            db,
            Mock.Of<ICurrentUserService>(u => u.UserName == "operator"),
            new FixedClock(),
            new OpenPeriod(),
            stockLock);

        var act = async () => await handler.Handle(new MarkDeliveryDeliveredCommand(delivery.Id), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NegativeStockException>();

        // H6: the delivery must have taken the lock BEFORE reading the balance.
        stockLock.Dimensions.Should().ContainSingle(k => k.ItemId == item.Id && k.ProductionOrderId == order.Id);
        db.InventoryTransactions.Count(t => t.SourceDocumentType == DocumentType.Delivery).Should().Be(0);
    }
}

/// <summary>
/// Ready-goods category regression: delivery must deduct only the selected grade,
/// even when the same production order has balances in several categories.
/// </summary>
public class DeliveryReadyGoodsCategoryTests
{
    [Fact]
    public async Task Delivery_DeductsOnlyTheSelectedReadyGoodsCategory()
    {
        var customer = new Customer("C-CAT", "Customer", "seed");
        var item = new Item("IT-CAT", "Fabric", default(UnitOfMeasure), "seed");
        var order = new ProductionOrder("PRD-CAT", customer.Id, item.Id, DateTime.UtcNow, "seed");
        var warehouse = new Warehouse("RDY-CAT", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var firstTransfer = new ReadyGoodsTransfer(
            "RGT-FIRST", DateTime.UtcNow, order.Id, customer.Id, item.Id, warehouse.Id,
            200m, null, "seed", category: ReadyGoodsCategory.FirstGrade);
        var secondTransfer = new ReadyGoodsTransfer(
            "RGT-SECOND", DateTime.UtcNow, order.Id, customer.Id, item.Id, warehouse.Id,
            100m, null, "seed", category: ReadyGoodsCategory.SecondGrade);

        var options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"delivery-category-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var db = new Persistence.ApplicationDbContext(options);
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.ProductionOrders.Add(order);
        db.Warehouses.Add(warehouse);
        db.ReadyGoodsTransfers.AddRange(firstTransfer, secondTransfer);
        db.InventoryTransactions.AddRange(
            new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, firstTransfer.TransferNumber, firstTransfer.Id, DateTime.UtcNow,
                warehouse.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
                quantityKg: 200m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed",
                readyGoodsCategory: ReadyGoodsCategory.FirstGrade),
            new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, secondTransfer.TransferNumber, secondTransfer.Id, DateTime.UtcNow,
                warehouse.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
                quantityKg: 100m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed",
                readyGoodsCategory: ReadyGoodsCategory.SecondGrade));

        var delivery = new Delivery("DLV-CAT", DateTime.UtcNow, customer.Id, "seed");
        delivery.AddLine(order.Id, item.Id, null, 60m, null, null, null, null,
            category: ReadyGoodsCategory.SecondGrade);
        delivery.MarkPrepared();
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var handler = new MarkDeliveryDeliveredCommandHandler(
            db,
            Mock.Of<ICurrentUserService>(u => u.UserName == "operator"),
            new FixedClock(),
            new OpenPeriod(),
            new RecordingStockLock());

        await handler.Handle(new MarkDeliveryDeliveredCommand(delivery.Id), CancellationToken.None);

        var posted = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.Delivery && t.SourceDocumentId == delivery.Id)
            .ToListAsync();
        posted.Should().ContainSingle();
        posted[0].Direction.Should().Be(TransactionDirection.Out);
        posted[0].ReadyGoodsCategory.Should().Be(ReadyGoodsCategory.SecondGrade);

        var readyRows = await db.InventoryTransactions
            .Where(t => t.WarehouseId == warehouse.Id && t.ProductionOrderId == order.Id && t.RawMessageId == null)
            .ToListAsync();
        readyRows.Where(t => t.ReadyGoodsCategory == ReadyGoodsCategory.FirstGrade)
            .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction).Should().Be(200m);
        readyRows.Where(t => t.ReadyGoodsCategory == ReadyGoodsCategory.SecondGrade)
            .Sum(t => (t.QuantityKg ?? 0m) * (int)t.Direction).Should().Be(40m);
    }
}

/// <summary>
/// H4: a receipt may only be settled against an invoice belonging to the
/// customer named in the request, and the guard must fire before anything is
/// written.
/// </summary>
public class CreateReceiptOwnershipTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public CreateReceiptOwnershipTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"receipt-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private (Guid invoiceOwner, Guid otherCustomer, Guid account, Guid invoice) Seed()
    {
        var owner = new Customer("C-OWNER", "Owner", "seed");
        var other = new Customer("C-OTHER", "Other", "seed");
        var account = new TreasuryAccount("BANK-1", "Main Bank", TreasuryAccountKind.Bank, "seed");
        var invoice = new Invoice("INV-1", DateTime.UtcNow, owner.Id, "seed");
        invoice.AddLine(null, Guid.NewGuid(), null, 1m, 100m, UnitOfMeasure.KG, null);
        invoice.Issue();

        using var db = NewDb();
        db.Customers.AddRange(owner, other);
        db.TreasuryAccounts.Add(account);
        db.Invoices.Add(invoice);
        db.SaveChanges();

        return (owner.Id, other.Id, account.Id, invoice.Id);
    }

    private CreateReceiptCommandHandler Handler(Persistence.ApplicationDbContext db, RecordingStockLock stockLock) => new(
        db,
        Mock.Of<ICurrentUserService>(u => u.UserName == "cashier"),
        new FixedNumberGenerator(),
        new OpenPeriod(),
        stockLock);

    [Fact]
    public async Task Receipt_For_Another_Customers_Invoice_Is_Rejected_And_Writes_Nothing()
    {
        var (_, otherCustomer, account, invoice) = Seed();
        using var db = NewDb();
        var stockLock = new RecordingStockLock();

        var act = async () => await Handler(db, stockLock).Handle(
            new CreateReceiptCommand(DateTime.UtcNow, account, 50m, otherCustomer, invoice, "cash", null),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Message.Should().Contain("does not match invoice");

        using var verify = NewDb();
        verify.Receipts.Should().BeEmpty();
        verify.TreasuryTransactions.Should().BeEmpty();
        verify.CustomerLedgerEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Matching_Customer_Takes_The_Invoice_Lock_And_Posts_Exactly_One_Leg_Per_Ledger()
    {
        var (owner, _, account, invoice) = Seed();
        using var db = NewDb();
        var stockLock = new RecordingStockLock();

        var dto = await Handler(db, stockLock).Handle(
            new CreateReceiptCommand(DateTime.UtcNow, account, 50m, owner, invoice, "cash", null),
            CancellationToken.None);

        dto.Amount.Should().Be(50m);

        // H4 race guard: the payment window is serialized on the invoice itself.
        stockLock.NamedResources.Should().ContainSingle(r => r == $"Invoice:{invoice}");

        using var verify = NewDb();
        verify.Receipts.Should().ContainSingle();
        verify.TreasuryTransactions.Should().ContainSingle();
        verify.CustomerLedgerEntries.Should().ContainSingle();
    }
}

/// <summary>
/// R5: a receipt that names an invoice but no customer must be credited to the
/// INVOICE's customer - otherwise the invoice is settled while the customer's
/// statement never moves.
/// </summary>
public class CreateReceiptCustomerInferenceTests : IDisposable
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options =
        new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"receipt-infer-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    [Fact]
    public async Task Missing_Customer_Is_Inferred_From_The_Invoice()
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var account = new TreasuryAccount("BANK-1", "Main Bank", TreasuryAccountKind.Bank, "seed");
        var invoice = new Invoice("INV-1", DateTime.UtcNow, customer.Id, "seed");
        invoice.AddLine(null, Guid.NewGuid(), null, 1m, 100m, UnitOfMeasure.KG, null);
        invoice.Issue();

        using (var db = new Persistence.ApplicationDbContext(_options))
        {
            db.Customers.Add(customer);
            db.TreasuryAccounts.Add(account);
            db.Invoices.Add(invoice);
            db.SaveChanges();
        }

        using var handlerDb = new Persistence.ApplicationDbContext(_options);
        var handler = new CreateReceiptCommandHandler(
            handlerDb,
            Mock.Of<ICurrentUserService>(u => u.UserName == "cashier"),
            new FixedNumberGenerator(),
            new OpenPeriod(),
            new RecordingStockLock());

        // CustomerId is deliberately null in the request.
        var dto = await handler.Handle(
            new CreateReceiptCommand(DateTime.UtcNow, account.Id, 50m, null, invoice.Id, "cash", null),
            CancellationToken.None);

        dto.CustomerId.Should().Be(customer.Id);

        using var verify = new Persistence.ApplicationDbContext(_options);
        var ledger = verify.CustomerLedgerEntries.Single();
        ledger.CustomerId.Should().Be(customer.Id);
        ledger.Credit.Should().Be(50m);
    }

    public void Dispose() { }
}

/// <summary>
/// R2: a delivery may only contain production orders that belong to the
/// delivery's own customer.
/// </summary>
public class CreateDeliveryCustomerOwnershipTests : IDisposable
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options =
        new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"delivery-own-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static CreateDeliveryCommandHandler Handler(Persistence.ApplicationDbContext db) => new(
        db,
        Mock.Of<ICurrentUserService>(u => u.UserName == "planner"),
        new FixedNumberGenerator());

    [Fact]
    public async Task Order_Belonging_To_Another_Customer_Is_Rejected()
    {
        var mine = new Customer("C-MINE", "Mine", "seed");
        var theirs = new Customer("C-THEIRS", "Theirs", "seed");
        var item = new Item("IT-1", "Item", default(UnitOfMeasure), "seed");
        var theirOrder = new ProductionOrder("PRD-THEIRS", theirs.Id, item.Id, DateTime.UtcNow, "seed");

        using (var db = new Persistence.ApplicationDbContext(_options))
        {
            db.Customers.AddRange(mine, theirs);
            db.Items.Add(item);
            db.ProductionOrders.Add(theirOrder);
            db.SaveChanges();
        }

        using var handlerDb = new Persistence.ApplicationDbContext(_options);
        var command = new CreateDeliveryCommand(
            mine.Id, DateTime.UtcNow, null,
            new List<DeliveryLineInput>
            {
                new() { ProductionOrderId = theirOrder.Id, ItemId = item.Id, QuantityKg = 10m }
            });

        var act = async () => await Handler(handlerDb).Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Message.Should().Contain("does not belong to the delivery's customer");

        using var verify = new Persistence.ApplicationDbContext(_options);
        verify.Deliveries.Should().BeEmpty();
    }

    public void Dispose() { }
}

/// <summary>
/// H5: the live session lookup that the JWT validation event relies on. It must
/// report the CURRENT active flag and permission set, not what the token says.
/// </summary>
public class UserSessionValidatorTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options;

    public UserSessionValidatorTests()
    {
        _options = new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"session-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    [Fact]
    public async Task Returns_Live_Active_State_And_Roles()
    {
        var user = new User("admin", "hash", "Admin", "admin,inventory.view", "seed");
        using (var db = new Persistence.ApplicationDbContext(_options))
        {
            db.Users.Add(user);
            db.SaveChanges();
        }

        using var verify = new Persistence.ApplicationDbContext(_options);
        var state = await new UserSessionValidator(verify).GetSessionStateAsync(user.Id);

        state.Should().NotBeNull();
        state!.IsActive.Should().BeTrue();
        state.Roles.Should().BeEquivalentTo(new[] { "admin", "inventory.view" });
    }

    [Fact]
    public async Task Deactivated_User_Is_Reported_As_Inactive_Immediately()
    {
        var user = new User("admin", "hash", "Admin", "admin", "seed");
        using (var db = new Persistence.ApplicationDbContext(_options))
        {
            db.Users.Add(user);
            db.SaveChanges();
        }

        using (var db = new Persistence.ApplicationDbContext(_options))
        {
            db.Users.First(u => u.Id == user.Id).Deactivate("other-admin");
            db.SaveChanges();
        }

        using var verify = new Persistence.ApplicationDbContext(_options);
        var state = await new UserSessionValidator(verify).GetSessionStateAsync(user.Id);

        state!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_User_Returns_Null_So_The_Token_Cannot_Be_Resurrected()
    {
        using var db = new Persistence.ApplicationDbContext(_options);
        var state = await new UserSessionValidator(db).GetSessionStateAsync(Guid.NewGuid());
        state.Should().BeNull();
    }
}

/// <summary>
/// H7: the receiving-inspection REJECTION path. A rejected quantity accumulates on
/// the receipt line, is capped by the received quantity, and posts one correcting OUT
/// ledger row - so the delta a request posts is only correct against the COMMITTED line
/// state. These tests pin the handler behaviour (the rejection is recorded, the cap
/// holds) and the concurrency fix: the aggregate is re-read INSIDE the lot lock, so a
/// request that lost the race can no longer post a second correcting row.
/// </summary>
public class RawInspectionRejectionConcurrencyTests
{
    private readonly DbContextOptions<Persistence.ApplicationDbContext> _options =
        new DbContextOptionsBuilder<Persistence.ApplicationDbContext>()
            .UseInMemoryDatabase($"inspection-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private Persistence.ApplicationDbContext NewDb() => new(_options);

    private (Guid MessageId, Guid WarehouseId, Guid CustomerId, Guid ItemId, Guid LineId) SeedMessage(decimal receivedKg)
    {
        var customer = new Customer("C-INSPECT", "Inspection Customer", "seed");
        var warehouse = new Warehouse("RAW-INSPECT", "Raw Warehouse", WarehouseKind.RawMaterial, "seed");
        // The line's item must be a real row: recording a rejection validates the rejected
        // quantity against the item's configured units (spec sections 5-6).
        var item = new Item("IT-INSPECT", "Inspection Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var message = new RawMessage("RAW-2026-INSPECT", DateTime.UtcNow, customer.Id, warehouse.Id, "receiver", "seed");
        var line = message.AddLine(item.Id, receivedKg, null, null, null);

        using var db = NewDb();
        db.Customers.Add(customer);
        db.Warehouses.Add(warehouse);
        db.Items.Add(item);
        db.RawMessages.Add(message);
        db.InventoryTransactions.Add(new InventoryTransaction(DocumentType.RawReceiptMessage, message.MessageNumber, message.Id,
            DateTime.UtcNow, warehouse.Id, customer.Id, line.ItemId, message.Id, null, receivedKg, null, TransactionDirection.In, "seed"));
        db.SaveChanges();

        return (message.Id, warehouse.Id, customer.Id, line.ItemId, line.Id);
    }

    private static RecordInspectionCommandHandler Handler(
        Persistence.ApplicationDbContext db, IAllocationLockService stockLock) => new(
        db,
        Mock.Of<ICurrentUserService>(u => u.UserName == "inspector"),
        new FixedClock(),
        new OpenPeriod(),
        stockLock);

    private static RecordInspectionCommand Rejecting(
        Guid messageId, Guid lineId, decimal rejectedKg, string notes) => new(
        messageId, InspectionStatus.Rejected, notes,
        new List<RawMessageLineRejectionInput> { new() { LineId = lineId, RejectedQuantityKg = rejectedKg } });

    [Fact]
    public async Task A_Rejection_Is_Recorded_And_Posts_Exactly_One_Correcting_Out_Row()
    {
        var seed = SeedMessage(receivedKg: 100m);
        using var db = NewDb();
        var stockLock = new RecordingStockLock();

        await Handler(db, stockLock).Handle(
            Rejecting(seed.MessageId, seed.LineId, 30m, "torn fabric"), CancellationToken.None);

        // The lot dimension is resolved from the message's OWN lines, which only works when
        // the line graph is actually loaded. The previous implementation loaded the message
        // with FindAsync - which never populates a collection navigation - so there were no
        // lock dimensions and recording a rejection failed outright.
        stockLock.Dimensions.Should().ContainSingle(k =>
            k.WarehouseId == seed.WarehouseId && k.ItemId == seed.ItemId &&
            k.CustomerId == seed.CustomerId && k.RawMessageId == seed.MessageId);

        using var verify = NewDb();
        var outRows = verify.InventoryTransactions
            .Where(t => t.RawMessageId == seed.MessageId && t.Direction == TransactionDirection.Out)
            .ToList();

        outRows.Should().ContainSingle();
        outRows[0].QuantityKg.Should().Be(30m);
        verify.Set<RawMessageLine>().Single(l => l.Id == seed.LineId).RejectedQuantityKg.Should().Be(30m);
    }

    [Fact]
    public async Task A_Rejection_Beyond_What_Was_Received_Is_Refused_And_Adds_No_Second_Row()
    {
        var seed = SeedMessage(receivedKg: 100m);

        // The first inspection committed 60 KG of rejections on the line.
        using (var first = NewDb())
            await Handler(first, new RecordingStockLock()).Handle(
                Rejecting(seed.MessageId, seed.LineId, 60m, "first inspection"), CancellationToken.None);

        // A later inspection reporting another 60 KG (110 KG in total) must hit the cap.
        using var db = NewDb();
        var act = async () => await Handler(db, new RecordingStockLock()).Handle(
            Rejecting(seed.MessageId, seed.LineId, 60m, "second inspection"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Message.Should().Contain("cannot exceed the received quantity");

        using var verify = NewDb();
        verify.InventoryTransactions.Count(t => t.RawMessageId == seed.MessageId && t.Direction == TransactionDirection.Out)
            .Should().Be(1);
        verify.Set<RawMessageLine>().Single(l => l.Id == seed.LineId).RejectedQuantityKg.Should().Be(60m);
    }

    [Fact]
    public async Task Rejection_Cannot_Deduct_Raw_That_Was_Already_Consumed()
    {
        var seed = SeedMessage(100m);
        using (var consumed = NewDb())
        {
            consumed.InventoryTransactions.Add(new InventoryTransaction(DocumentType.ProductionOrder, "CONSUMED", Guid.NewGuid(),
                DateTime.UtcNow, seed.WarehouseId, seed.CustomerId, seed.ItemId, seed.MessageId, Guid.NewGuid(), 90m, null, TransactionDirection.Out, "operator"));
            await consumed.SaveChangesAsync();
        }
        using var db = NewDb();
        var act = async () => await Handler(db, new RecordingStockLock()).Handle(
            Rejecting(seed.MessageId, seed.LineId, 20m, "too late"), default);
        await act.Should().ThrowAsync<NegativeStockException>();
        using var verify = NewDb();
        verify.Set<RawMessageLine>().Single(l => l.Id == seed.LineId).RejectedQuantityKg.Should().BeNull();
        verify.InventoryTransactions.Count().Should().Be(2);
    }

    [Fact]
    public async Task A_Request_That_Lost_The_Race_Computes_Its_Delta_From_The_Committed_Line()
    {
        var seed = SeedMessage(receivedKg: 100m);
        using var db = NewDb();

        // The competing inspection commits at the exact moment this request takes its lot
        // lock: the snapshot this request read before the lock is already stale, which is the
        // real SQL Server race expressed deterministically for the InMemory provider.
        var stockLock = new CommitOnAcquireLock(async () =>
        {
            using var competing = NewDb();
            await Handler(competing, new RecordingStockLock()).Handle(
                Rejecting(seed.MessageId, seed.LineId, 60m, "won the race"), CancellationToken.None);
        });

        var act = async () => await Handler(db, stockLock).Handle(
            Rejecting(seed.MessageId, seed.LineId, 50m, "lost the race"), CancellationToken.None);

        // 60 committed + 50 reported > 100 received: the delta must be judged against what is
        // committed, not against the pre-lock snapshot (which saw nothing rejected).
        await act.Should().ThrowAsync<DomainException>();

        using var verify = NewDb();
        verify.InventoryTransactions.Count(t => t.RawMessageId == seed.MessageId && t.Direction == TransactionDirection.Out)
            .Should().Be(1, "only the request that committed may correct the customer's raw stock");
        verify.Set<RawMessageLine>().Single(l => l.Id == seed.LineId).RejectedQuantityKg.Should().Be(60m);
    }

    /// <summary>
    /// Stands in for the SQL Server lot lock while injecting the competitor at the moment the
    /// lock is taken. It proves the HANDLER re-reads; the real sp_getapplock behaviour it
    /// stands in for is proven by the SQL Server integration suite, never here.
    /// </summary>
    private sealed class CommitOnAcquireLock : IAllocationLockService
    {
        private readonly Func<Task> _onAcquire;

        public CommitOnAcquireLock(Func<Task> onAcquire) => _onAcquire = onAcquire;

        public async Task<IAsyncDisposable> AcquireAsync(StockLockKey key, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireManyAsync(IEnumerable<StockLockKey> keys, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireNamedAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        public async Task<IAsyncDisposable> AcquireExclusiveAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }
        public async Task<IAsyncDisposable> AcquireSharedAsync(string resource, CancellationToken cancellationToken = default)
        {
            await _onAcquire();
            return Noop.Instance;
        }

        private sealed class Noop : IAsyncDisposable
        {
            public static readonly Noop Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
