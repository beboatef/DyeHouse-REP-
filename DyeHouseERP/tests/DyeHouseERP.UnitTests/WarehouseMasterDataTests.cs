using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Warehouses.Commands;
using DyeHouseERP.Application.Warehouses.Queries;
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
/// Warehouse master data is DYNAMIC (spec section 15): an administrator creates, renames,
/// re-classifies and deactivates warehouses at runtime, with no code change and no fixed
/// list anywhere. These are handler-level tests against a real DbContext, because the
/// list/search filtering and the "deactivation is a flag, never a removal" rule are both
/// decided in the application and persistence layers.
/// </summary>
public class WarehouseMasterDataTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public WarehouseMasterDataTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"warehouse-master-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static CreateWarehouseCommandHandler CreateHandler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static UpdateWarehouseCommandHandler UpdateHandler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static SetWarehouseActiveCommandHandler ActiveHandler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static GetWarehousesQueryHandler ListHandler(ApplicationDbContext db) => new(db);

    [Fact]
    public async Task CreatingAWarehouse_PersistsItAndFindsItThroughTheListQuery()
    {
        using var db = NewDb();

        var created = await CreateHandler(db).Handle(
            new CreateWarehouseCommand("RAW-01", "Raw material store", WarehouseKind.RawMaterial), CancellationToken.None);

        created.Id.Should().NotBe(Guid.Empty);
        created.IsActive.Should().BeTrue("a warehouse is usable the moment it is created");

        var warehouses = await ListHandler(db).Handle(new GetWarehousesQuery(), CancellationToken.None);
        warehouses.Should().ContainSingle();
        warehouses[0].Id.Should().Be(created.Id);
        warehouses[0].Code.Should().Be("RAW-01");
        warehouses[0].Name.Should().Be("Raw material store");
        warehouses[0].Kind.Should().Be(WarehouseKind.RawMaterial);
    }

    [Fact]
    public async Task Search_FindsPartialCodeAndPartialName_AndTheKindFilterIsHonoured()
    {
        using var db = NewDb();
        await CreateHandler(db).Handle(new CreateWarehouseCommand("RAW-01", "Raw material store", WarehouseKind.RawMaterial), CancellationToken.None);
        await CreateHandler(db).Handle(new CreateWarehouseCommand("MAT-01", "Chemicals store", WarehouseKind.Materials), CancellationToken.None);
        await CreateHandler(db).Handle(new CreateWarehouseCommand("OPS-01", "Spare parts", WarehouseKind.OperatingSupplies), CancellationToken.None);

        var byPartialCode = await ListHandler(db).Handle(new GetWarehousesQuery(Search: "mat-"), CancellationToken.None);
        byPartialCode.Should().ContainSingle().Which.Code.Should().Be("MAT-01");

        var byPartialName = await ListHandler(db).Handle(new GetWarehousesQuery(Search: "CHEMICALS"), CancellationToken.None);
        byPartialName.Should().ContainSingle().Which.Code.Should().Be("MAT-01", "the search is case-insensitive over the name too");

        var byKind = await ListHandler(db).Handle(new GetWarehousesQuery(Kind: WarehouseKind.OperatingSupplies), CancellationToken.None);
        byKind.Should().ContainSingle().Which.Code.Should().Be("OPS-01");

        var everything = await ListHandler(db).Handle(new GetWarehousesQuery(), CancellationToken.None);
        everything.Should().HaveCount(3, "no search and no filter means the whole dynamic list");
    }

    [Fact]
    public async Task ActiveOnly_SeparatesDeactivatedWarehousesFromActiveOnes()
    {
        using var db = NewDb();
        var kept = await CreateHandler(db).Handle(new CreateWarehouseCommand("RAW-01", "Raw", WarehouseKind.RawMaterial), CancellationToken.None);
        var retired = await CreateHandler(db).Handle(new CreateWarehouseCommand("RAW-02", "Old raw", WarehouseKind.RawMaterial), CancellationToken.None);

        await ActiveHandler(db).Handle(new SetWarehouseActiveCommand(retired.Id, false), CancellationToken.None);

        var active = await ListHandler(db).Handle(new GetWarehousesQuery(ActiveOnly: true), CancellationToken.None);
        active.Should().ContainSingle().Which.Id.Should().Be(kept.Id);

        var inactive = await ListHandler(db).Handle(new GetWarehousesQuery(ActiveOnly: false), CancellationToken.None);
        inactive.Should().ContainSingle().Which.Id.Should().Be(retired.Id);
    }

    [Fact]
    public async Task UpdatingAWarehouse_RenamesItAndChangesItsKindInPlace()
    {
        using var db = NewDb();
        var created = await CreateHandler(db).Handle(
            new CreateWarehouseCommand("WRH-01", "Store one", WarehouseKind.Materials), CancellationToken.None);

        var updated = await UpdateHandler(db).Handle(
            new UpdateWarehouseCommand(created.Id, "Main chemicals store", WarehouseKind.OperatingSupplies), CancellationToken.None);

        updated.Name.Should().Be("Main chemicals store");
        updated.Kind.Should().Be(WarehouseKind.OperatingSupplies);
        updated.Code.Should().Be("WRH-01", "the code is the identity documents carry and is never edited");

        var reloaded = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == created.Id);
        reloaded.Name.Should().Be("Main chemicals store");
        reloaded.Kind.Should().Be(WarehouseKind.OperatingSupplies);
        reloaded.ModifiedBy.Should().Be("tester");
    }

    [Fact]
    public async Task DeactivatingThenActivating_AWarehouseFlipsOnlyTheFlag()
    {
        using var db = NewDb();
        var created = await CreateHandler(db).Handle(
            new CreateWarehouseCommand("RAW-01", "Raw store", WarehouseKind.RawMaterial), CancellationToken.None);

        var deactivated = await ActiveHandler(db).Handle(new SetWarehouseActiveCommand(created.Id, false), CancellationToken.None);
        deactivated.IsActive.Should().BeFalse();

        var reloaded = await db.Warehouses.AsNoTracking().SingleAsync(w => w.Id == created.Id);
        reloaded.IsActive.Should().BeFalse();
        reloaded.Code.Should().Be("RAW-01", "deactivation never rewrites or deletes the row");
        reloaded.Name.Should().Be("Raw store");

        var reactivated = await ActiveHandler(db).Handle(new SetWarehouseActiveCommand(created.Id, true), CancellationToken.None);
        reactivated.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnknownWarehouse_CannotBeUpdatedOrDeactivated()
    {
        using var db = NewDb();
        var missing = Guid.NewGuid();

        var update = () => UpdateHandler(db).Handle(
            new UpdateWarehouseCommand(missing, "Anything", WarehouseKind.RawMaterial), CancellationToken.None);
        await update.Should().ThrowAsync<NotFoundException>();

        var deactivate = () => ActiveHandler(db).Handle(new SetWarehouseActiveCommand(missing, false), CancellationToken.None);
        await deactivate.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreatingAWarehouseWithADuplicateCode_IsRefused()
    {
        using var db = NewDb();
        await CreateHandler(db).Handle(new CreateWarehouseCommand("RAW-01", "First", WarehouseKind.RawMaterial), CancellationToken.None);

        var duplicate = () => CreateHandler(db).Handle(
            new CreateWarehouseCommand("RAW-01", "Second", WarehouseKind.Materials), CancellationToken.None);

        await duplicate.Should().ThrowAsync<DuplicateCodeException>();
        (await db.Warehouses.CountAsync()).Should().Be(1, "the refused create must not leave a second row behind");
    }

    [Fact]
    public void ABlankCodeOrName_IsRejectedByTheValidator()
    {
        var validator = new CreateWarehouseCommandValidator();

        validator.Validate(new CreateWarehouseCommand("", "Raw store", WarehouseKind.RawMaterial)).IsValid.Should().BeFalse();
        validator.Validate(new CreateWarehouseCommand("RAW-01", "   ", WarehouseKind.RawMaterial)).IsValid.Should().BeFalse();
        validator.Validate(new CreateWarehouseCommand("RAW-01", "Raw store", WarehouseKind.RawMaterial)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivatingAWarehouse_KeepsItsStockHistoryAndBalanceReadable()
    {
        using var db = NewDb();
        var warehouse = await CreateHandler(db).Handle(
            new CreateWarehouseCommand("RAW-01", "Raw store", WarehouseKind.RawMaterial), CancellationToken.None);

        var material = new Material("MAT-1", "Caustic soda", MaterialUnit.KG, 10m, "tester");
        db.Materials.Add(material);
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseOrder, "PO-1", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, null, 50m, MaterialTransactionDirection.In, 5m, "tester"));
        await db.SaveChangesAsync();

        var ledger = new MaterialLedgerService(db);
        (await ledger.GetBalanceAsync(material.Id, warehouse.Id)).Should().Be(50m);

        await ActiveHandler(db).Handle(new SetWarehouseActiveCommand(warehouse.Id, false), CancellationToken.None);

        // The warehouse is out of use for NEW documents, but nothing about its past changed:
        // the row, its ledger rows and its balance are all still there and still readable.
        (await db.Warehouses.CountAsync(w => w.Id == warehouse.Id)).Should().Be(1, "deactivation never deletes the warehouse");
        (await db.MaterialTransactions.CountAsync(t => t.WarehouseId == warehouse.Id)).Should().Be(1, "its stock history is untouched");
        (await ledger.GetBalanceAsync(material.Id, warehouse.Id)).Should().Be(50m, "its balance is derived from the ledger and unchanged");

        var retired = await ListHandler(db).Handle(new GetWarehousesQuery(ActiveOnly: false), CancellationToken.None);
        retired.Should().ContainSingle().Which.Code.Should().Be("RAW-01", "a retired warehouse is still listed, just not active");
    }
}
