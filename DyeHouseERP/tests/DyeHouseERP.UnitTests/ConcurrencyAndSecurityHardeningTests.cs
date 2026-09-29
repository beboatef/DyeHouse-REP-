using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.Commands;
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

    public Task<IAsyncDisposable> AcquireAsync(Guid rawMessageId, Guid itemId, Guid customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IAsyncDisposable>(Noop.Instance);

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
            StockLockKey.ReadyLot(warehouse, item, customer, Guid.NewGuid()).Resource,
            StockLockKey.MaterialLot(warehouse, item).Resource
        };

        resources.Distinct().Should().HaveCount(resources.Length);
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
        invoice.AddLine(null, Guid.NewGuid(), null, 1m, 100m, null);
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
