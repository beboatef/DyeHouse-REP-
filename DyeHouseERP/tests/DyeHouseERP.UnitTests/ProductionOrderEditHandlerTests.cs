using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Handler-level cover for the Job Order edit workflow (spec sections 12 + 16): a real command handler
/// against a real DbContext, so the persisted result - not just the in-memory entity - is asserted.
/// </summary>
public class ProductionOrderEditHandlerTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ProductionOrderEditHandlerTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"po-edit-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static UpdateProductionOrderCommandHandler Handler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("planner"));

    private static async Task<Guid> SeedOpenOrderAsync(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "tester");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "tester");
        db.AddRange(customer, item,
            new ProductionStageDefinition("PS001", "Formation", 1, "seed", isFormationStage: true));
        await db.SaveChangesAsync();

        var dto = await new CreateProductionOrderCommandHandler(
                db, new StubCurrentUser("tester"), new FixedNumberGenerator())
            .Handle(new CreateProductionOrderCommand(
                customer.Id, item.Id, DateTime.UtcNow, "Navy", 1000m, null, "Lot A", "CR-1", "first note",
                ProductionPriority.Normal),
                CancellationToken.None);

        return dto.Id;
    }

    [Fact]
    public async Task Update_PersistsTheDescriptiveFields_AndNeverTouchesIdentityOrQuantity()
    {
        using var db = NewDb();
        var orderId = await SeedOpenOrderAsync(db);
        var before = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        var newDate = DateTime.UtcNow.Date.AddDays(-3);

        var dto = await Handler(db).Handle(new UpdateProductionOrderCommand(
            orderId, newDate, "Red", "Lot B", "CR-2", "corrected", ProductionPriority.Urgent, JobOrderType.OpenLine),
            CancellationToken.None);

        dto.Color.Should().Be("Red");
        dto.RawOrigin.Should().Be("Lot B");
        dto.CustomerReference.Should().Be("CR-2");
        dto.Notes.Should().Be("corrected");
        dto.Priority.Should().Be(ProductionPriority.Urgent);
        dto.JobOrderType.Should().Be(JobOrderType.OpenLine);
        dto.OrderDate.Should().Be(newDate);

        var stored = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        stored.CustomerId.Should().Be(before.CustomerId);
        stored.ItemId.Should().Be(before.ItemId);
        stored.RequestedQuantityKg.Should().Be(1000m);
    }

    [Fact]
    public async Task Update_OnACompletedOrder_IsRefused_AndPersistsNothing()
    {
        using var db = NewDb();
        var orderId = await SeedOpenOrderAsync(db);

        var order = await db.ProductionOrders.Include(o => o.StageExecutions).SingleAsync(o => o.Id == orderId);
        order.StageExecutions.Single().Skip(stageAllowsSkip: true, reason: "scrapped", modifiedBy: "operator");
        order.CompleteWithReason("closed with no output");
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new UpdateProductionOrderCommand(
            orderId, DateTime.UtcNow, "Red", null, null, null, ProductionPriority.Low, JobOrderType.ClosedLine),
            CancellationToken.None);

        await act.Should().ThrowAsync<DocumentLockedException>();
        (await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Color.Should().Be("Navy");
    }

    [Fact]
    public async Task Update_WhileInProduction_AllowsTheNotes_And_ARealLineTypeChange()
    {
        using var db = NewDb();
        var orderId = await SeedOpenOrderAsync(db);

        var order = await db.ProductionOrders.SingleAsync(o => o.Id == orderId);
        // The new allocation row is registered explicitly, exactly as AllocateRawCommandHandler does -
        // a client-generated key on a child of an already-tracked aggregate is otherwise taken for an
        // UPDATE against a row that was never inserted.
        var allocation = order.AllocateRaw(Guid.NewGuid(), order.ItemId, 1000m, null, "operator");
        db.RawAllocations.Add(allocation);
        order.MarkInProduction();
        await db.SaveChangesAsync();

        // Re-sending the current line type must never fail an unrelated notes edit.
        var dto = await Handler(db).Handle(new UpdateProductionOrderCommand(
            orderId, order.OrderDate, order.Color, order.RawOrigin, order.CustomerReference,
            "second note", ProductionPriority.High, JobOrderType.ClosedLine),
            CancellationToken.None);
        dto.Notes.Should().Be("second note");
        dto.Priority.Should().Be(ProductionPriority.High);

        // An actual change is allowed while the order is running: the line type is a classification
        // (nothing posted is derived from it), so a mistake is corrected by an ordinary edit that the
        // permission layer already guards and the audit trail records.
        var changed = await Handler(db).Handle(new UpdateProductionOrderCommand(
            orderId, order.OrderDate, order.Color, order.RawOrigin, order.CustomerReference,
            "second note", ProductionPriority.High, JobOrderType.OpenLine),
            CancellationToken.None);
        changed.JobOrderType.Should().Be(JobOrderType.OpenLine);
        (await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).JobOrderType
            .Should().Be(JobOrderType.OpenLine);
    }

    [Fact]
    public void UpdateValidator_RefusesOverlongFields_AndAnUnknownEnum()
    {
        var validator = new UpdateProductionOrderCommandValidator();

        validator.Validate(new UpdateProductionOrderCommand(Guid.NewGuid(), DateTime.UtcNow,
            new string('x', 101), null, null, null, ProductionPriority.Normal, JobOrderType.ClosedLine))
            .IsValid.Should().BeFalse("colour is capped at 100 characters");

        validator.Validate(new UpdateProductionOrderCommand(Guid.Empty, DateTime.UtcNow,
            null, null, null, null, ProductionPriority.Normal, JobOrderType.ClosedLine))
            .IsValid.Should().BeFalse("the order id is required");

        validator.Validate(new UpdateProductionOrderCommand(Guid.NewGuid(), DateTime.UtcNow,
            "Red", null, null, null, (ProductionPriority)99, JobOrderType.ClosedLine))
            .IsValid.Should().BeFalse("an undefined priority is refused");

        validator.Validate(new UpdateProductionOrderCommand(Guid.NewGuid(), DateTime.UtcNow,
            "Red", "Lot", "CR", "note", ProductionPriority.High, JobOrderType.OpenLine))
            .IsValid.Should().BeTrue();
    }
}
