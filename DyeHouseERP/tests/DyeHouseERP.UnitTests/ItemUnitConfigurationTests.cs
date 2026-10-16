using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.CustomerReturns.Commands;
using DyeHouseERP.Application.Deliveries.Commands;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.RawReceipts.Commands;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Structured primary/secondary units on the item master (spec sections 5-6).
///
/// These tests pin the pure domain rules AND the server-side enforcement in the
/// real handlers, because a validator-only rule would leave the actual posting
/// path unprotected. They also prove the two quantities stay INDEPENDENT - no
/// KG&lt;-&gt;Meter conversion is ever introduced.
/// </summary>
public class ItemUnitConfigurationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ItemUnitConfigurationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"item-units-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    // =====================================================================
    // 1-4: the four supported configurations
    // =====================================================================

    [Fact]
    public void KgPrimary_MeterSecondary_BothRequired_ExposesBothUnits()
    {
        var item = new Item("RAW", "Raw fabric", UnitOfMeasure.KG, "seed",
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: true);

        item.BaseUnit.Should().Be(UnitOfMeasure.KG);
        item.SecondaryUnit.Should().Be(UnitOfMeasure.Meter);
        item.RequireBothUnits.Should().BeTrue();
        item.ConfiguredUnits.Should().BeEquivalentTo(new[] { UnitOfMeasure.KG, UnitOfMeasure.Meter });
    }

    [Fact]
    public void MeterPrimary_KgSecondary_BothRequired_ExposesBothUnits()
    {
        var item = new Item("FAB", "Fabric", UnitOfMeasure.Meter, "seed",
            secondaryUnit: UnitOfMeasure.KG, requireBothUnits: true);

        item.BaseUnit.Should().Be(UnitOfMeasure.Meter);
        item.SecondaryUnit.Should().Be(UnitOfMeasure.KG);
        item.ConfiguredUnits.Should().BeEquivalentTo(new[] { UnitOfMeasure.Meter, UnitOfMeasure.KG });
    }

    [Fact]
    public void SingleUnitItem_AllowsOnlyItsOwnUnit()
    {
        var item = new Item("CHEM", "Chemical", UnitOfMeasure.KG, "seed");

        item.SecondaryUnit.Should().BeNull();
        item.RequireBothUnits.Should().BeFalse();
        item.UsesUnit(UnitOfMeasure.KG).Should().BeTrue();
        item.UsesUnit(UnitOfMeasure.Meter).Should().BeFalse();
    }

    [Fact]
    public void SecondaryConfiguredButNotRequired_AllowsEitherQuantity()
    {
        var item = new Item("RAW2", "Raw fabric", UnitOfMeasure.KG, "seed",
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: false);

        // Only the KG is supplied - valid, because both units are not required.
        var act = () => ItemUnitRules.EnsureValidQuantities(
            item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits, 500m, null, item.Code);

        act.Should().NotThrow();
        // ...and a Meter-only movement is equally valid.
        var meterOnly = () => ItemUnitRules.EnsureValidQuantities(
            item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits, null, 1800m, item.Code);
        meterOnly.Should().NotThrow();
    }

    // =====================================================================
    // 8-9: configuration guards
    // =====================================================================

    [Fact]
    public void SecondaryUnit_EqualToBaseUnit_IsRejected()
    {
        var act = () => new Item("BAD", "Bad", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.KG);
        (act.Should().Throw<DomainException>()).WithMessage("*different*");
    }

    [Fact]
    public void RequireBothUnits_WithoutSecondaryUnit_IsRejected()
    {
        var act = () => new Item("BAD2", "Bad", UnitOfMeasure.KG, "seed", requireBothUnits: true);
        (act.Should().Throw<DomainException>()).WithMessage("*secondary unit*");
    }

    // =====================================================================
    // 5, 7, 10: quantity validation and the no-conversion guarantee
    // =====================================================================

    [Fact]
    public void Quantity_InAnUnsupportedUnit_IsRejected_NeverConverted()
    {
        // A KG-only item must never accept a Meter quantity (and vice versa).
        var act = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, null, false, null, 1800m, "ITM-1");
        (act.Should().Throw<DomainException>()).WithMessage("*not configured to use Meter*");

        var reverse = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.Meter, null, false, 500m, null, "ITM-2");
        (reverse.Should().Throw<DomainException>()).WithMessage("*not configured to use KG*");
    }

    [Fact]
    public void RequireBothUnits_MissingTheSecondary_IsRejected()
    {
        var act = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, true, 500m, null, "ITM-1");
        (act.Should().Throw<DomainException>()).WithMessage("*BOTH*");
    }

    [Fact]
    public void RequireBothUnits_MissingTheBaseUnit_IsRejected()
    {
        // The flag means BOTH of the item's units, so which one the operator left out must not change
        // the verdict. Only the secondary used to be checked, which let a Meter-only quantity through
        // for an item whose rule is "KG AND Meter" while the KG-only quantity was correctly refused.
        var act = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, true, null, 1800m, "ITM-1");
        (act.Should().Throw<DomainException>())
            .WithMessage("*requires BOTH of its units*")
            .And.Message.Should().Contain("Missing: KG");

        // The mirrored configuration (Meter primary) is symmetrical: what is missing here is the base.
        var mirrored = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.Meter, UnitOfMeasure.KG, true, 500m, null, "ITM-2");
        (mirrored.Should().Throw<DomainException>())
            .WithMessage("*requires BOTH of its units*")
            .And.Message.Should().Contain("Missing: Meter");
    }

    [Fact]
    public void RequireBothUnits_IsSatisfiedOnlyBySupplyingBothUnits()
    {
        // Both supplied - in either unit order - is the only shape that passes.
        var kgPrimary = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, true, 500m, 1800m, "ITM-1");
        kgPrimary.Should().NotThrow();

        var meterPrimary = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.Meter, UnitOfMeasure.KG, true, 500m, 1800m, "ITM-2");
        meterPrimary.Should().NotThrow();

        // With neither quantity supplied, the more fundamental refusal wins: a workflow that needs a
        // quantity says so first (pre-existing precedence, unchanged).
        var neither = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, true, null, null, "ITM-1");
        (neither.Should().Throw<DomainException>()).And.Message.Should().Contain("quantity is required");

        // On a path that tolerates an empty figure (requireAnyQuantity: false, as the production-floor
        // fields use), the both-units rule speaks instead, and it names BOTH units as missing.
        var neitherOnAnEmptyTolerantPath = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, true, null, null, "ITM-1",
            context: null, requireAnyQuantity: false);
        (neitherOnAnEmptyTolerantPath.Should().Throw<DomainException>())
            .And.Message.Should().Contain("Missing: KG and Meter");
    }

    [Fact]
    public void AnItemWithoutASecondaryUnit_IsNeverAskedForOne()
    {
        // "Require both units" needs a second unit to speak of; an item that has one unit must not be
        // handed a demand for a unit it does not use (and no quantity is ever converted to invent one).
        var act = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, null, false, 500m, null, "ITM-1");
        act.Should().NotThrow();
    }

    [Fact]
    public void WorkflowThatNeedsAQuantity_RejectsAnEmptyMovement()
    {
        var act = () => ItemUnitRules.EnsureValidQuantities(
            UnitOfMeasure.KG, UnitOfMeasure.Meter, false, null, null, "ITM-1");
        (act.Should().Throw<DomainException>()).WithMessage("*quantity is required*");
    }

    [Fact]
    public void NewItem_DefaultsToSingleUnit_ForBackwardCompatibility()
    {
        // Migration/back-compat: an existing item (created without the new arguments)
        // keeps a single unit, SecondaryUnit null and RequireBothUnits false.
        var item = new Item("OLD", "Legacy item", UnitOfMeasure.KG, "seed");
        item.SecondaryUnit.Should().BeNull();
        item.RequireBothUnits.Should().BeFalse();
    }

    // =====================================================================
    // Item commands: creation, and unit changes with inventory history
    // =====================================================================

    [Fact]
    public async Task CreateItemCommand_PersistsSecondaryUnitAndRequireBothUnits()
    {
        using var db = NewDb();

        var dto = await new CreateItemCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new CreateItemCommand("ITM-1", "Raw", UnitOfMeasure.KG, NameAr: "خام", NameEn: "Raw",
                SecondaryUnit: UnitOfMeasure.Meter, RequireBothUnits: true),
            CancellationToken.None);

        dto.SecondaryUnit.Should().Be(UnitOfMeasure.Meter);
        dto.RequireBothUnits.Should().BeTrue();

        var persisted = await db.Items.AsNoTracking().SingleAsync(i => i.Id == dto.Id);
        persisted.SecondaryUnit.Should().Be(UnitOfMeasure.Meter);
        persisted.RequireBothUnits.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateItemCommand_RefusesUnitChange_WhenInventoryHistoryExists()
    {
        using var db = NewDb();
        var item = new Item("ITM-1", "Raw", UnitOfMeasure.KG, "seed");
        db.Items.Add(item);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, "RMSG-1", Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), item.Id, null, null, 500m, null,
            TransactionDirection.In, "seed"));
        await db.SaveChangesAsync();

        var act = () => new UpdateItemCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new UpdateItemCommand(item.Id, null, null, null, UnitOfMeasure.Meter, null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*cannot be changed*");

        var reloaded = await db.Items.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        reloaded.BaseUnit.Should().Be(UnitOfMeasure.KG, "the historical quantities must not be reinterpreted");
    }

    [Fact]
    public async Task UpdateItemCommand_AllowsRequireBothToggle_WithInventoryHistory()
    {
        using var db = NewDb();
        var item = new Item("ITM-2", "Raw", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        db.Items.Add(item);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, "RMSG-2", Guid.NewGuid(), DateTime.UtcNow,
            Guid.NewGuid(), Guid.NewGuid(), item.Id, null, null, 500m, 1800m,
            TransactionDirection.In, "seed"));
        await db.SaveChangesAsync();

        var dto = await new UpdateItemCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new UpdateItemCommand(item.Id, null, null, null, null, null, RequireBothUnits: true),
            CancellationToken.None);

        dto.RequireBothUnits.Should().BeTrue();
        dto.BaseUnit.Should().Be(UnitOfMeasure.KG);
    }

    // =====================================================================
    // Real posting handlers enforce the item's configured units
    // =====================================================================

    private static (Customer Customer, Warehouse Warehouse, Item Item) SeedMasterData(
        ApplicationDbContext db, UnitOfMeasure baseUnit,
        UnitOfMeasure? secondaryUnit = null, bool requireBothUnits = false)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var item = new Item("ITM-1", "Raw", baseUnit, "seed",
            secondaryUnit: secondaryUnit, requireBothUnits: requireBothUnits);
        db.AddRange(customer, warehouse, item);
        db.SaveChanges();
        return (customer, warehouse, item);
    }

    [Fact]
    public async Task RawReceipt_RefusesAQuantityInAnUnconfiguredUnit()
    {
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG);

        var act = () => new CreateRawMessageCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock(), new InventoryLedgerService(db))
            .Handle(new CreateRawMessageCommand(DateTime.UtcNow, customer.Id, warehouse.Id, null,
                new List<RawMessageLineInput> { new() { ItemId = item.Id, QuantityMeter = 1800m } }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");
        (await db.RawMessages.CountAsync()).Should().Be(0, "a refused receipt posts nothing");
    }

    [Fact]
    public async Task RawReceipt_RequiresBothUnits_WhenTheItemAsksForThem()
    {
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG,
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: true);

        var act = () => new CreateRawMessageCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock(), new InventoryLedgerService(db))
            .Handle(new CreateRawMessageCommand(DateTime.UtcNow, customer.Id, warehouse.Id, null,
                new List<RawMessageLineInput> { new() { ItemId = item.Id, QuantityKg = 500m } }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*BOTH*");
        (await db.RawMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RawReceipt_RequiresBothUnits_EvenWhenOnlyTheSecondaryUnitWasTyped()
    {
        // The mirror image of the test above, at the handler level: typing the Meter alone is NOT
        // "both units" either. Before the fix this receipt was accepted and posted a movement whose
        // KG side was silently missing, while the KG-only receipt was refused.
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG,
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: true);

        var act = () => new CreateRawMessageCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock(), new InventoryLedgerService(db))
            .Handle(new CreateRawMessageCommand(DateTime.UtcNow, customer.Id, warehouse.Id, null,
                new List<RawMessageLineInput> { new() { ItemId = item.Id, QuantityMeter = 1800m } }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*BOTH*");
        (await db.RawMessages.CountAsync()).Should().Be(0, "a refused receipt posts nothing");
    }

    [Fact]
    public async Task RawReceipt_PostsIndependentKgAndMeter_AndTheLedgerKeepsThemSeparate()
    {
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG, secondaryUnit: UnitOfMeasure.Meter);

        await new CreateRawMessageCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock(), new InventoryLedgerService(db))
            .Handle(new CreateRawMessageCommand(DateTime.UtcNow, customer.Id, warehouse.Id, null,
                new List<RawMessageLineInput> { new() { ItemId = item.Id, QuantityKg = 500m, QuantityMeter = 1800m } }),
                CancellationToken.None);

        var message = await db.RawMessages.AsNoTracking().SingleAsync();
        var ledger = new InventoryLedgerService(db);
        var (kg, meter) = await ledger.GetMessageBalanceAsync(message.Id, item.Id, warehouse.Id);

        kg.Should().Be(500m);
        meter.Should().Be(1800m, "the two measurements are independent - no conversion happened");
    }

    [Fact]
    public async Task ProductionOrderRequestedQuantity_RefusesAnUnconfiguredUnit()
    {
        using var db = NewDb();
        var (customer, _, item) = SeedMasterData(db, UnitOfMeasure.KG);

        var act = () => new CreateProductionOrderCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator())
            .Handle(new CreateProductionOrderCommand(
                customer.Id, item.Id, DateTime.UtcNow, null,
                RequestedQuantityKg: null, RequestedQuantityMeter: 1200m,
                RawOrigin: null, CustomerReference: null, Notes: null, Priority: ProductionPriority.Normal),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");
    }

    [Fact]
    public async Task Delivery_RefusesAQuantityInAnUnconfiguredUnit()
    {
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG);
        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 100m);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var act = () => new CreateDeliveryCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator())
            .Handle(new CreateDeliveryCommand(customer.Id, DateTime.UtcNow, null,
                new List<DeliveryLineInput> { new() { ProductionOrderId = order.Id, ItemId = item.Id, QuantityMeter = 90m } }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");
        _ = warehouse;
    }

    [Fact]
    public async Task CustomerReturn_RequiresTheSecondaryUnit_WhenTheItemAsksForBoth()
    {
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG,
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: true);

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, 500m, 1800m, null, null);
        db.RawMessages.Add(message);
        await db.SaveChangesAsync();

        var act = () => new CreateCustomerReturnCommandHandler(
                db, new StubCurrentUser("operator"), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock())
            .Handle(new CreateCustomerReturnCommand(customer.Id, DateTime.UtcNow, null, null,
                new List<CustomerReturnLineInput>
                {
                    new(RawMessageId: message.Id, ItemId: item.Id, QuantityKg: 10m, QuantityMeter: null)
                }),
                CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*BOTH*");
        (await db.CustomerReturns.CountAsync()).Should().Be(0);
    }

    // =====================================================================
    // Excel import/export mappings
    // =====================================================================

    [Fact]
    public void ItemImportTemplate_ExposesTheStructuredUnitColumns()
    {
        ItemImportTemplate.Headers.Should().Contain(new[] { "BaseUnit", "SecondaryUnit", "RequireBothUnits" });
    }

    [Fact]
    public async Task ItemImport_Preview_ReportsRowSpecificUnitErrors()
    {
        using var db = NewDb();
        var headers = new List<string> { "Code", "NameEn", "BaseUnit", "SecondaryUnit", "RequireBothUnits" };
        var reader = new StubExcelReader(headers, new List<Dictionary<string, string?>>
        {
            // invalid base unit
            new() { ["Code"] = "ITM-1", ["NameEn"] = "A", ["BaseUnit"] = "Top", ["SecondaryUnit"] = "", ["RequireBothUnits"] = "No" },
            // duplicate primary/secondary
            new() { ["Code"] = "ITM-2", ["NameEn"] = "B", ["BaseUnit"] = "KG", ["SecondaryUnit"] = "KG", ["RequireBothUnits"] = "No" },
            // require both without a secondary unit
            new() { ["Code"] = "ITM-3", ["NameEn"] = "C", ["BaseUnit"] = "KG", ["SecondaryUnit"] = "", ["RequireBothUnits"] = "Yes" },
            // valid two-unit row
            new() { ["Code"] = "ITM-4", ["NameEn"] = "D", ["BaseUnit"] = "KG", ["SecondaryUnit"] = "Meter", ["RequireBothUnits"] = "Yes" }
        });

        var preview = await new PreviewItemImportCommandHandler(db, reader).Handle(
            new PreviewItemImportCommand(new byte[] { 1 }), CancellationToken.None);

        preview.TotalRows.Should().Be(4);
        preview.Rows.Single(r => r.RowNumber == 2).IsValid.Should().BeFalse();
        preview.Rows.Single(r => r.RowNumber == 2).Errors.Should().ContainMatch("*BaseUnit*");
        preview.Rows.Single(r => r.RowNumber == 3).Errors.Should().ContainMatch("*same as BaseUnit*");
        preview.Rows.Single(r => r.RowNumber == 4).Errors.Should().ContainMatch("*no SecondaryUnit*");
        preview.Rows.Single(r => r.RowNumber == 5).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ItemImport_Execute_PersistsTheStructuredUnitConfiguration()
    {
        using var db = NewDb();
        var headers = new List<string> { "Code", "NameEn", "BaseUnit", "SecondaryUnit", "RequireBothUnits" };
        var reader = new StubExcelReader(headers, new List<Dictionary<string, string?>>
        {
            new() { ["Code"] = "ITM-9", ["NameEn"] = "Raw", ["BaseUnit"] = "KG", ["SecondaryUnit"] = "Meter", ["RequireBothUnits"] = "Yes" }
        });

        var result = await new ExecuteItemImportCommandHandler(db, reader, new StubCurrentUser("admin")).Handle(
            new ExecuteItemImportCommand(new byte[] { 1 }), CancellationToken.None);

        result.Created.Should().Be(1);
        var item = await db.Items.AsNoTracking().SingleAsync();
        item.BaseUnit.Should().Be(UnitOfMeasure.KG);
        item.SecondaryUnit.Should().Be(UnitOfMeasure.Meter);
        item.RequireBothUnits.Should().BeTrue();
    }

    [Fact]
    public async Task RawIssue_RefusesAQuantityInAnUnconfiguredUnit()
    {
        // The raw ISSUE is now the only way raw material is taken out of a customer's lot (the separate
        // "allocate to an existing order" step was removed), so the unit gate is pinned on that path: a
        // Meter quantity may never be issued for a KG-only item, and nothing at all is posted when it is.
        using var db = NewDb();
        var (customer, warehouse, item) = SeedMasterData(db, UnitOfMeasure.KG);

        var message = new RawMessage("RMSG-ISSUE", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, null, 1800m, null, null);
        db.RawMessages.Add(message);
        db.ProductionStageDefinitions.Add(
            new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true));
        await db.SaveChangesAsync();

        var act = () => new IssueRawMaterialCommandHandler(
                db, new StubCurrentUser("operator"), new InventoryLedgerService(db), new FixedNumberGenerator(),
                new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new IssueRawMaterialCommand(customer.Id, item.Id, message.Id, DateTime.UtcNow,
                ActualQuantityKg: null, ActualQuantityMeter: 500m), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");
        (await db.ProductionOrders.CountAsync()).Should().Be(0, "a refused issue creates no order");
        (await db.RawAllocations.CountAsync()).Should().Be(0, "a refused issue records no allocation");
        (await db.InventoryTransactions.CountAsync(t => t.Direction == TransactionDirection.Out)).Should().Be(0);
    }

    private sealed class StubExcelReader : IExcelImportReader
    {
        private readonly List<string> _headers;
        private readonly List<Dictionary<string, string?>> _rows;

        public StubExcelReader(List<string> headers, List<Dictionary<string, string?>> rows)
        {
            _headers = headers;
            _rows = rows;
        }

        public List<Dictionary<string, string?>> ReadRows(Stream fileStream, out List<string> headers)
        {
            headers = _headers;
            return _rows;
        }
    }
}
