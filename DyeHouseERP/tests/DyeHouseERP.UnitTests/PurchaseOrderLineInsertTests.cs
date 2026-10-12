using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H-2d regression cover: a line added to a purchase order that ALREADY exists must be INSERTed. Before the fix
/// EF change detection took the new line (key generated client-side) for an existing row and saved an UPDATE
/// against a row that was never inserted, so SaveChangesAsync threw DbUpdateConcurrencyException - HTTP 409 and
/// the line was lost. Handler-level tests against a real DbContext, because the defect lives in change tracking.
/// </summary>
public class PurchaseOrderLineInsertTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PurchaseOrderLineInsertTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"purchase-line-insert-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>An order created through the real create handler, persisted, with <paramref name="linesAtCreation"/> lines.</summary>
    private static async Task<(Guid OrderId, List<Guid> MaterialIds, Mock<ISender> Mediator)> SeedPersistedOrderAsync(
        ApplicationDbContext db, int linesAtCreation)
    {
        var supplier = new Supplier("SUP-1", "Supplier", "tester");
        var warehouse = new Warehouse("RAW", "Raw", WarehouseKind.RawMaterial, "tester");
        var materials = Enumerable.Range(1, 3)
            .Select(i => new Material($"MAT-{i}", $"Chemical {i}", MaterialUnit.KG, 10m, "tester")).ToList();

        db.AddRange(supplier, warehouse);
        db.AddRange(materials);
        await db.SaveChangesAsync();

        var mediator = new Mock<ISender>();

        var lines = materials.Take(linesAtCreation)
            .Select(m => new PurchaseOrderLineInput
            {
                MaterialId = m.Id, Quantity = 10m, Unit = MaterialUnit.KG, UnitPrice = 5m
            }).ToList();

        await new CreatePurchaseOrderCommandHandler(
                db, new StubCurrentUser("tester"), new FixedNumberGenerator(), mediator.Object)
            .Handle(new CreatePurchaseOrderCommand(DateTime.UtcNow, supplier.Id, warehouse.Id, Lines: lines),
                CancellationToken.None);

        return ((await db.PurchaseOrders.SingleAsync()).Id, materials.Select(m => m.Id).ToList(), mediator);
    }

    private static Task AddLineAsync(ApplicationDbContext db, Mock<ISender> mediator, Guid orderId, Guid materialId,
        decimal quantity = 10m, decimal unitPrice = 5m)
        => new AddPurchaseOrderLineCommandHandler(db, new StubCurrentUser("tester"), mediator.Object)
            .Handle(new AddPurchaseOrderLineCommand(orderId, materialId, quantity, MaterialUnit.KG, unitPrice),
                CancellationToken.None);

    private static Task<PurchaseOrder> ReloadAsync(ApplicationDbContext db, Guid orderId)
        => db.PurchaseOrders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId);

    [Fact]
    public async Task AddingALineToAPersistedOrder_InsertsItAndUpdatesTheDerivedOrderTotal()
    {
        using var db = NewDb();
        var (orderId, materialIds, mediator) = await SeedPersistedOrderAsync(db, linesAtCreation: 0);
        (await ReloadAsync(db, orderId)).Lines.Should().BeEmpty("the order started with no line");

        await AddLineAsync(db, mediator, orderId, materialIds[0]);

        var order = await ReloadAsync(db, orderId);
        order.Lines.Should().HaveCount(1, "the new line must be inserted, never dropped");
        order.Lines.Single().MaterialId.Should().Be(materialIds[0]);
        order.Lines.Single().Quantity.Should().Be(10m);
        order.TotalValue.Should().Be(50m, "the order total is derived from its lines");
    }

    [Fact]
    public async Task AddingLinesInSeparateEdits_StoresEachLineExactlyOnce()
    {
        using var db = NewDb();
        var (orderId, materialIds, mediator) = await SeedPersistedOrderAsync(db, linesAtCreation: 1);

        await AddLineAsync(db, mediator, orderId, materialIds[1], quantity: 4m, unitPrice: 2.5m);
        await AddLineAsync(db, mediator, orderId, materialIds[2], quantity: 7m, unitPrice: 3m);

        var order = await ReloadAsync(db, orderId);
        order.Lines.Should().HaveCount(3, "each edit inserts exactly its own line - nothing is duplicated");
        order.Lines.Select(l => l.MaterialId).Should().OnlyHaveUniqueItems();
        order.Lines.Select(l => l.Quantity).Should().BeEquivalentTo(new[] { 10m, 4m, 7m });
        order.TotalValue.Should().Be(50m + 10m + 21m);
    }

    [Fact]
    public async Task AddingALine_KeepsTheLinesTheOrderWasCreatedWith()
    {
        using var db = NewDb();
        var (orderId, materialIds, mediator) = await SeedPersistedOrderAsync(db, linesAtCreation: 2);
        var original = (await ReloadAsync(db, orderId)).Lines.Select(l => (l.Id, l.Quantity)).ToList();

        await AddLineAsync(db, mediator, orderId, materialIds[2], quantity: 1m, unitPrice: 1m);

        var order = await ReloadAsync(db, orderId);
        order.Lines.Should().HaveCount(3, "adding a line must never rewrite or lose the existing ones");
        order.Lines.Select(l => (l.Id, l.Quantity)).Should().Contain(original, "the original lines are untouched");
    }
}
