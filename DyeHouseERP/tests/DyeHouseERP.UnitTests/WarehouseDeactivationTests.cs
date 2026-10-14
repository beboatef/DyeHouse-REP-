using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.MaterialSales.Commands;
using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Application.RawReceipts.Commands;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Application.Supplies.Commands;
using DyeHouseERP.Application.Warehouses.Commands;
using DyeHouseERP.Application.Warehouses.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Warehouse master data is dynamic (spec section 15): an administrator deactivates a
/// warehouse from inside the ERP at any time. Deactivation is a FLAG - the row, its ledger
/// rows, its balances and every document that already refers to it must survive - so these
/// tests pin both halves of that contract:
///
///   1. a NEW document may not use a deactivated warehouse (it is refused, and nothing is
///      written), and
///   2. everything that ALREADY exists keeps working: a draft can still be completed, the
///      ledger rows and balances are untouched, and the warehouse is still listed.
///
/// The refusal itself lives in one place, WarehouseGuard, which every new-document path
/// calls; the supply issue, material sale, raw receipt, purchase order and material
/// transfer paths below are the call sites that used to accept any warehouse id.
/// </summary>
public class WarehouseDeactivationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public WarehouseDeactivationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"warehouse-deactivation-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static StubCurrentUser User() => new("tester");

    private static Task DeactivateAsync(ApplicationDbContext db, Guid warehouseId) =>
        new SetWarehouseActiveCommandHandler(db, User())
            .Handle(new SetWarehouseActiveCommand(warehouseId, false), CancellationToken.None);

    // ------------------------------------------------------------------ new documents

    [Fact]
    public async Task CreatingASupplyIssueInADeactivatedWarehouse_IsRefused()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("SUPPLIES", "Supplies Store", WarehouseKind.OperatingSupplies, "tester");
        db.Add(warehouse);
        await db.SaveChangesAsync();
        await DeactivateAsync(db, warehouse.Id);

        var act = () => new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(DateTime.UtcNow, warehouse.Id, "Maintenance", "Spare parts"),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.SupplyIssues.CountAsync()).Should().Be(0, "a refused request writes nothing");
    }

    [Fact]
    public async Task CreatingAMaterialSaleFromADeactivatedWarehouse_IsRefused()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("MATERIALS", "Materials Store", WarehouseKind.Materials, "tester");
        db.Add(warehouse);
        await db.SaveChangesAsync();
        await DeactivateAsync(db, warehouse.Id);

        var act = () => new CreateMaterialSaleCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateMaterialSaleCommand(DateTime.UtcNow, warehouse.Id, "Walk-in buyer"),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialSales.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReceivingRawMaterialIntoADeactivatedWarehouse_IsRefused()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("RAW-1", "Raw Store", WarehouseKind.RawMaterial, "tester");
        db.Add(warehouse);
        await db.SaveChangesAsync();
        await DeactivateAsync(db, warehouse.Id);

        var act = () => new CreateRawMessageCommandHandler(
                db, User(), new FixedNumberGenerator(), new FixedClock(), new OpenPeriod(), new RecordingStockLock(),
                new DyeHouseERP.Persistence.Services.InventoryLedgerService(db))
            .Handle(new CreateRawMessageCommand(
                DateTime.UtcNow, Guid.NewGuid(), warehouse.Id, null,
                new List<RawMessageLineInput> { new() { ItemId = Guid.NewGuid(), QuantityKg = 10m } }),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.RawMessages.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync()).Should().Be(0, "no stock movement is written either");
    }

    [Fact]
    public async Task CreatingAPurchaseOrderForADeactivatedWarehouse_IsRefused()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("RAW-1", "Raw Store", WarehouseKind.RawMaterial, "tester");
        var supplier = new Supplier("SUP-1", "Supplier One", "tester");
        db.AddRange(warehouse, supplier);
        await db.SaveChangesAsync();
        await DeactivateAsync(db, warehouse.Id);

        var act = () => new CreatePurchaseOrderCommandHandler(
                db, User(), new FixedNumberGenerator(), Mock.Of<ISender>())
            .Handle(new CreatePurchaseOrderCommand(DateTime.UtcNow, supplier.Id, warehouse.Id),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.PurchaseOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TransferringMaterialIsRefusedWhenEitherEndIsDeactivated()
    {
        using var db = NewDb();
        var source = new Warehouse("MAT-A", "Materials A", WarehouseKind.Materials, "tester");
        var target = new Warehouse("MAT-B", "Materials B", WarehouseKind.Materials, "tester");
        var material = new Material("MAT-1", "Dye Red", MaterialUnit.KG, 25m, "tester");
        db.AddRange(source, target, material);
        await db.SaveChangesAsync();
        await DeactivateAsync(db, target.Id);

        var handler = new CreateMaterialTransferCommandHandler(
            db, User(), new FixedNumberGenerator(), new MaterialLedgerService(db),
            new FixedClock(), new OpenPeriod(), new RecordingStockLock());

        var intoDeactivated = () => handler.Handle(
            new CreateMaterialTransferCommand(material.Id, source.Id, target.Id, 5m, null), CancellationToken.None);
        await intoDeactivated.Should().ThrowAsync<DomainException>();

        // The other direction is refused too: the deactivated warehouse cannot be emptied.
        var activate = new SetWarehouseActiveCommandHandler(db, User());
        await activate.Handle(new SetWarehouseActiveCommand(target.Id, true), CancellationToken.None);
        await DeactivateAsync(db, source.Id);

        var outOfDeactivated = () => handler.Handle(
            new CreateMaterialTransferCommand(material.Id, source.Id, target.Id, 5m, null), CancellationToken.None);
        await outOfDeactivated.Should().ThrowAsync<DomainException>();

        (await db.MaterialTransfers.CountAsync()).Should().Be(0, "no transfer document is written");
        (await db.MaterialTransactions.CountAsync()).Should().Be(0, "and no ledger pair either");
    }

    [Fact]
    public async Task UsingAWarehouseThatDoesNotExist_IsNotFound()
    {
        using var db = NewDb();

        var act = () => new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(DateTime.UtcNow, Guid.NewGuid(), "Maintenance", "Spare parts"),
                CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------------------------------- an OLD document is never a way around the rule

    [Fact]
    public async Task PostingAnExistingDraftOnADeactivatedWarehouse_IsRefused()
    {
        using var db = NewDb();
        var (issueId, materialId, warehouseId) = await SeedDraftIssueAsync(db);

        // Deactivating the warehouse AFTER the draft was typed must not become a loophole:
        // posting is what writes the OUT rows, so the movement check applies here too.
        await DeactivateAsync(db, warehouseId);

        var act = () => PostAsync(db, issueId);

        await act.Should().ThrowAsync<DomainException>();
        (await db.SupplyIssues.SingleAsync()).Status.Should().Be(SupplyIssueStatus.Draft,
            "a refused post leaves the document exactly as it was - it is not half-posted");
        (await new MaterialLedgerService(db).GetBalanceAsync(materialId, warehouseId))
            .Should().Be(100m, "and writes no movement: the balance is untouched");
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issueId))
            .Should().Be(0, "not one OUT row was written");
    }

    [Fact]
    public async Task AddingALineToAnExistingDocument_IsRefusedWhenItsWarehouseIsDeactivated()
    {
        using var db = NewDb();
        var (issueId, materialId, warehouseId) = await SeedDraftIssueAsync(db);
        await DeactivateAsync(db, warehouseId);

        var act = () => new AddSupplyIssueLineCommandHandler(db, new MaterialLedgerService(db))
            .Handle(new AddSupplyIssueLineCommand(issueId, materialId, 5m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.SupplyIssueLines.CountAsync()).Should().Be(1,
            "the line the draft already had is untouched and the refused one was never stored");
        (await db.SupplyIssueLines.SingleAsync()).Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task AddingALineToAnExistingMaterialSale_IsRefusedWhenItsWarehouseIsDeactivated()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("MATERIALS", "Materials Store", WarehouseKind.Materials, "tester");
        var material = new Material("MAT-C1", "Dye Red", MaterialUnit.KG, 25m, "tester");
        db.AddRange(warehouse, material);
        await db.SaveChangesAsync();
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, 100m, MaterialTransactionDirection.In, 25m, "tester"));
        await db.SaveChangesAsync();

        var sale = await new CreateMaterialSaleCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateMaterialSaleCommand(DateTime.UtcNow, warehouse.Id, "Walk-in buyer"), CancellationToken.None);
        await DeactivateAsync(db, warehouse.Id);

        var act = () => new AddMaterialSaleLineCommandHandler(db, new MaterialLedgerService(db))
            .Handle(new AddMaterialSaleLineCommand(sale.Id, material.Id, 2m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.MaterialSaleLines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CancellingAPostedDocument_IsRefused_BecauseTheReversalWritesANewMovement()
    {
        using var db = NewDb();
        var (issueId, materialId, warehouseId) = await SeedDraftIssueAsync(db);
        await PostAsync(db, issueId);
        await DeactivateAsync(db, warehouseId);

        var act = () => new CancelSupplyIssueCommandHandler(db, User(), new FixedClock(), new OpenPeriod())
            .Handle(new CancelSupplyIssueCommand(issueId, "typist error"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.SupplyIssues.SingleAsync()).Status.Should().Be(SupplyIssueStatus.Posted,
            "the cancellation is refused whole: no status flip and no half-written reversal");
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issueId))
            .Should().Be(1, "the single OUT row from the posting is the only movement that exists");
    }

    // -------------------------------------- a document with NO new movement still works

    [Fact]
    public async Task CancellingA_Draft_OnADeactivatedWarehouse_IsAllowed_BecauseItWritesNoMovement()
    {
        using var db = NewDb();
        var (issueId, _, warehouseId) = await SeedDraftIssueAsync(db);
        await DeactivateAsync(db, warehouseId);

        // A draft was never posted, so cancelling it writes no ledger row at all - the
        // warehouse has been retired, not the work. Refusing this would strand the document.
        var cancelled = await new CancelSupplyIssueCommandHandler(db, User(), new FixedClock(), new OpenPeriod())
            .Handle(new CancelSupplyIssueCommand(issueId, "no longer required"), CancellationToken.None);

        cancelled.Id.Should().Be(issueId);
        (await db.SupplyIssues.SingleAsync()).Status.Should().Be(SupplyIssueStatus.Cancelled);
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == issueId))
            .Should().Be(0, "nothing was written to any ledger for this document");
        (await new GetWarehousesQueryHandler(db).Handle(new GetWarehousesQuery(), CancellationToken.None))
            .Should().ContainSingle(w => w.Id == warehouseId && !w.IsActive,
                "the retired warehouse is still listed and still readable");
    }

    [Fact]
    public async Task ReadingDocumentsAndBalancesOfADeactivatedWarehouse_KeepsWorking()
    {
        using var db = NewDb();
        var (issueId, materialId, warehouseId) = await SeedDraftIssueAsync(db);
        await PostAsync(db, issueId);
        var balanceBefore = await new MaterialLedgerService(db).GetBalanceAsync(materialId, warehouseId);

        await DeactivateAsync(db, warehouseId);

        // History stays fully readable: the document, its lines and the derived balance.
        var issue = await db.SupplyIssues.Include(i => i.Lines).SingleAsync();
        issue.Status.Should().Be(SupplyIssueStatus.Posted);
        issue.Lines.Should().HaveCount(1);
        (await new MaterialLedgerService(db).GetBalanceAsync(materialId, warehouseId)).Should().Be(balanceBefore);
    }

    // ---------------------------------------------------------------------- helpers

    /// <summary>A warehouse with stock, a material, and a DRAFT supply issue carrying one line.</summary>
    private static async Task<(Guid IssueId, Guid MaterialId, Guid WarehouseId)> SeedDraftIssueAsync(
        ApplicationDbContext db, decimal stock = 100m)
    {
        var warehouse = new Warehouse("SUPPLIES", "Supplies Store", WarehouseKind.OperatingSupplies, "tester");
        var material = new Material("MAT-S1", "Winding tape", MaterialUnit.KG, 10m, "tester", MaterialKind.OperatingSupply);
        db.AddRange(warehouse, material);
        await db.SaveChangesAsync();
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, stock, MaterialTransactionDirection.In, 10m, "tester"));
        await db.SaveChangesAsync();

        var issue = await new CreateSupplyIssueCommandHandler(db, User(), new FixedNumberGenerator())
            .Handle(new CreateSupplyIssueCommand(DateTime.UtcNow, warehouse.Id, "Maintenance", "Spare parts"),
                CancellationToken.None);
        await new AddSupplyIssueLineCommandHandler(db, new MaterialLedgerService(db))
            .Handle(new AddSupplyIssueLineCommand(issue.Id, material.Id, 5m), CancellationToken.None);

        return (issue.Id, material.Id, warehouse.Id);
    }

    private static Task PostAsync(ApplicationDbContext db, Guid issueId) =>
        new PostSupplyIssueCommandHandler(
                db, User(), new MaterialLedgerService(db), new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new PostSupplyIssueCommand(issueId), CancellationToken.None);

    [Fact]
    public async Task DeactivatingAWarehouseLeavesItsMovementsBalancesAndListingIntact()
    {
        using var db = NewDb();
        var warehouse = new Warehouse("MAT-A", "Materials A", WarehouseKind.Materials, "tester");
        var material = new Material("MAT-1", "Dye Red", MaterialUnit.KG, 25m, "tester");
        db.AddRange(warehouse, material);
        await db.SaveChangesAsync();
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, 40m, MaterialTransactionDirection.In, 25m, "tester"));
        await db.SaveChangesAsync();

        var balanceBefore = await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id);
        await DeactivateAsync(db, warehouse.Id);

        // Nothing was deleted, rewritten or zeroed by the flag flip.
        (await new MaterialLedgerService(db).GetBalanceAsync(material.Id, warehouse.Id)).Should().Be(balanceBefore);
        (await db.MaterialTransactions.CountAsync(t => t.WarehouseId == warehouse.Id)).Should().Be(1);
        (await db.Warehouses.CountAsync()).Should().Be(1);

        // And it is still visible in master data - with its state - so an administrator can
        // see it and restore it (it only drops out of the "active only" list).
        var all = await new GetWarehousesQueryHandler(db).Handle(new GetWarehousesQuery(), CancellationToken.None);
        all.Should().ContainSingle(w => w.Id == warehouse.Id && !w.IsActive);

        var active = await new GetWarehousesQueryHandler(db)
            .Handle(new GetWarehousesQuery(ActiveOnly: true), CancellationToken.None);
        active.Should().BeEmpty("a deactivated warehouse is not offered for new documents");

        var inactive = await new GetWarehousesQueryHandler(db)
            .Handle(new GetWarehousesQuery(ActiveOnly: false), CancellationToken.None);
        inactive.Should().ContainSingle(w => w.Id == warehouse.Id);
    }
}
