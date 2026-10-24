using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionStages.Commands;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Application.ProductionStages.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Dynamic stage administration (spec section 19). The twenty-one seeded operations are
/// STARTING DATA, never a maximum: every test here either adds a stage beyond them or
/// administers one, and none of them may need a source change. They are handler-level tests
/// against a real DbContext, because what matters is what the ERP does - the route, the flag,
/// the query, the transfer and the raw-material gate - not what one domain method returns.
/// </summary>
public class ProductionStageAdministrationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ProductionStageAdministrationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"stage-admin-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static CreateProductionStageDefinitionCommandHandler Creator(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static UpdateProductionStageDefinitionCommandHandler Updater(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static SetProductionStageActiveCommandHandler Activator(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static SetFormationStageCommandHandler FormationSetter(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    private static Task<ProductionStageDefinitionDto> CreateStageAsync(ApplicationDbContext db,
        string code, string name, int sequence, bool formation = false, string? notes = null)
        => Creator(db).Handle(new CreateProductionStageDefinitionCommand(
            code, name, sequence, true, true, notes, formation),
            CancellationToken.None);

    private static async Task SeedStartingStagesAsync(ApplicationDbContext db)
        => await ProductionStageSeeder.SeedAsync(db, NullLogger.Instance);

    private static Task<Guid> FormationStageIdAsync(ApplicationDbContext db)
        => db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsFormationStage).Select(s => s.Id).SingleAsync();

    private static Task<ProductionStageDefinitionDto> ActiveAsync(ApplicationDbContext db, Guid id, bool isActive)
        => Activator(db).Handle(new SetProductionStageActiveCommand(id, isActive), CancellationToken.None);

    private static async Task<List<ProductionStageDefinitionDto>> ListAsync(ApplicationDbContext db, bool? activeOnly = null, string? search = null)
        => await new GetProductionStageDefinitionsQueryHandler(db)
            .Handle(new GetProductionStageDefinitionsQuery(activeOnly, search), CancellationToken.None);

    /// <summary>A Job Order created through the real handler, so it snapshots the active route exactly as the API does.</summary>
    private static async Task<Guid> CreateOrderAsync(ApplicationDbContext db)
    {
        var customer = new Customer("C-ADM-1", "Customer", "tester");
        var item = new Item("IT-ADM-1", "Item", UnitOfMeasure.KG, "tester");
        db.AddRange(customer, item);
        await db.SaveChangesAsync();

        await new CreateProductionOrderCommandHandler(db, new StubCurrentUser("tester"), new FixedNumberGenerator())
            .Handle(new CreateProductionOrderCommand(
                customer.Id, item.Id, DateTime.UtcNow, "Navy", 1000m, null, null, null, null, default),
                CancellationToken.None);

        return (await db.ProductionOrders.AsNoTracking().SingleAsync()).Id;
    }

    // ============================================================ create + edit

    [Fact]
    public async Task AStageBeyondTheSeededTwentyOne_CanBeCreatedEditedAndIsServedByTheQuery()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);

        var created = await CreateStageAsync(db, "PS022", "طباعة رقمية", 22, notes: "مرحلة مضافة من داخل النظام");
        created.Code.Should().Be("PS022");
        created.IsActive.Should().BeTrue("a new stage is usable immediately");

        (await db.ProductionStageDefinitions.CountAsync()).Should().Be(23, "the original twenty-one stages plus Printing and the user-created stage must all remain");

        var updated = await Updater(db).Handle(new UpdateProductionStageDefinitionCommand(
            created.Id, "طباعة رقمية (معدلة)", 23, "ملاحظات محدثة"), CancellationToken.None);
        updated.Name.Should().Be("طباعة رقمية (معدلة)");
        updated.Sequence.Should().Be(23);

        var stored = await db.ProductionStageDefinitions.AsNoTracking().SingleAsync(s => s.Id == created.Id);
        stored.Name.Should().Be("طباعة رقمية (معدلة)");
        stored.Sequence.Should().Be(23);
        stored.Notes.Should().Be("ملاحظات محدثة");
        stored.Code.Should().Be("PS022", "the code is the stage's stable identity and is never rewritten");

        (await ListAsync(db)).Should().Contain(s => s.Id == created.Id);
    }

    [Fact]
    public async Task EditingAStageWithoutAValidNameOrOrder_IsRejected()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);

        var validator = new UpdateProductionStageDefinitionCommandValidator();

        validator.Validate(new UpdateProductionStageDefinitionCommand(Guid.NewGuid(), "  ", 5)).IsValid
            .Should().BeFalse("a stage name is required");
        validator.Validate(new UpdateProductionStageDefinitionCommand(Guid.NewGuid(), "Name", 0)).IsValid
            .Should().BeFalse("the stage order must be positive");
        validator.Validate(new UpdateProductionStageDefinitionCommand(Guid.Empty, "Name", 1)).IsValid
            .Should().BeFalse("the stage must be identified");
        validator.Validate(new UpdateProductionStageDefinitionCommand(Guid.NewGuid(), "Name", 1)).IsValid
            .Should().BeTrue();

        // The entity refuses it as well, so no other caller can bypass the rule.
        var stage = new ProductionStageDefinition("PS900", "Stage", 900, "tester");
        var act = () => stage.UpdateDetails("   ", 5, null, "tester");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task EditingAStageThatDoesNotExist_IsRefused()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);

        Func<Task> act = () => Updater(db).Handle(
            new UpdateProductionStageDefinitionCommand(Guid.NewGuid(), "أي اسم", 1), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreatingASecondStageWithTheSameCode_IsRefused()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);

        Func<Task> act = () => CreateStageAsync(db, "PS001", "اسم آخر", 1);

        await act.Should().ThrowAsync<DuplicateCodeException>();
    }

    // ============================================================ active / inactive

    [Fact]
    public async Task DeactivatingAStage_HidesItFromTheActiveListButKeepsItInMasterData()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        var stage = await CreateStageAsync(db, "PS023", "مرحلة مؤقتة", 23);

        var deactivated = await ActiveAsync(db, stage.Id, false);
        deactivated.IsActive.Should().BeFalse();

        (await ListAsync(db, activeOnly: true)).Should().NotContain(s => s.Id == stage.Id);
        (await ListAsync(db)).Should().Contain(s => s.Id == stage.Id,
            "deactivation is a flag - the row stays in master data");

        var restored = await ActiveAsync(db, stage.Id, true);
        restored.IsActive.Should().BeTrue();
        (await ListAsync(db, activeOnly: true)).Should().Contain(s => s.Id == stage.Id);
    }

    [Fact]
    public async Task TheTwoRouteMarkers_CannotBeDeactivated()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);

        var formationId = await db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsFormationStage).Select(s => s.Id).SingleAsync();
        var readyGoodsId = await db.ProductionStageDefinitions.AsNoTracking()
            .Where(s => s.IsReadyGoodsStage).Select(s => s.Id).SingleAsync();

        Func<Task> formation = () => ActiveAsync(db, formationId, false);
        Func<Task> readyGoods = () => ActiveAsync(db, readyGoodsId, false);

        await formation.Should().ThrowAsync<DomainException>(
            "every new Job Order starts at the formation stage");
        await readyGoods.Should().ThrowAsync<DomainException>(
            "the ready-goods stage is where output becomes finished stock");

        (await db.ProductionStageDefinitions.AsNoTracking().SingleAsync(s => s.Id == formationId))
            .IsActive.Should().BeTrue("a refused deactivation changes nothing");
    }

    [Fact]
    public async Task Search_MatchesPartialCodeAndPartialName()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        await CreateStageAsync(db, "PS022", "طباعة رقمية", 22);

        (await ListAsync(db, search: "PS022")).Should().ContainSingle(s => s.Code == "PS022");
        (await ListAsync(db, search: "رقمية")).Should().ContainSingle(s => s.Code == "PS022");
        (await ListAsync(db, search: "  ")).Should().HaveCount(23, "a blank search includes the original stages, Printing, and the user-created stage");
    }

    // ============================================================ route integration

    [Fact]
    public async Task ANewStage_CanTakeOverTheFormationRole_AndNewJobOrdersStartThere()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        var newStage = await CreateStageAsync(db, "PS022", "تشكيل جديد", 22);

        await FormationSetter(db).Handle(new SetFormationStageCommand(newStage.Id), CancellationToken.None);

        var stages = await db.ProductionStageDefinitions.AsNoTracking().ToListAsync();
        stages.Count(s => s.IsFormationStage).Should().Be(1, "exactly one stage is where a Job Order starts");
        stages.Single(s => s.IsFormationStage).Id.Should().Be(newStage.Id);

        var orderId = await CreateOrderAsync(db);
        var executions = await db.ProductionOrderStageExecutions.AsNoTracking()
            .Where(e => e.ProductionOrderId == orderId).ToListAsync();

        executions.Should().HaveCount(1,
            "only the starting stage is activated - the next one is chosen by the operator, never by a fixed route");
        executions.Single().StageDefinitionId.Should().Be(newStage.Id,
            "a stage added from inside the ERP is used by production exactly like a seeded one");
    }

    [Fact]
    public async Task AnInactiveStage_IsNeverChosenForANewTransfer()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        var parked = await CreateStageAsync(db, "PS024", "مرحلة موقوفة", 24);
        await ActiveAsync(db, parked.Id, false);

        (await db.ProductionStageDefinitions.AsNoTracking().Where(s => s.IsActive).Select(s => s.Id).ToListAsync())
            .Should().NotContain(parked.Id, "a deactivated stage leaves the active route");

        var orderId = await CreateOrderAsync(db);
        var execution = await db.ProductionOrderStageExecutions.AsNoTracking()
            .SingleAsync(e => e.ProductionOrderId == orderId);

        var transfer = new TransferToNextStageCommandHandler(
            db, new StubCurrentUser("tester"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator(), new RecordingStockLock());

        Func<Task> act = () => transfer.Handle(new TransferToNextStageCommand(
            execution.Id, parked.Id, 100m, null, null, null, null), CancellationToken.None);

        // Refused because the stage is inactive - not because of the allocation gate, which this
        // command checks later: an inactive stage is rejected before anything else can happen.
        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*inactive*");
    }

    [Fact]
    public async Task DeactivatingAStage_LeavesOldExecutionsAndTheirTraceabilityIntact()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        var newStage = await CreateStageAsync(db, "PS025", "تشكيل مؤقت", 25);

        await FormationSetter(db).Handle(new SetFormationStageCommand(newStage.Id), CancellationToken.None);
        var orderId = await CreateOrderAsync(db);
        var execution = await db.ProductionOrderStageExecutions.AsNoTracking()
            .SingleAsync(e => e.ProductionOrderId == orderId);
        execution.StageDefinitionId.Should().Be(newStage.Id);

        // Hand the starting role back to the seeded التشكيل stage, then park the new one.
        var seededFormation = (await db.ProductionStageDefinitions.AsNoTracking().SingleAsync(s => s.Code == "PS002")).Id;
        await FormationSetter(db).Handle(new SetFormationStageCommand(seededFormation), CancellationToken.None);

        (await FormationStageIdAsync(db)).Should().Be(seededFormation,
            "the flag moved instead of being duplicated, which is what makes the new stage parkable");

        await ActiveAsync(db, newStage.Id, false);

        var stored = await db.ProductionOrderStageExecutions.AsNoTracking().SingleAsync(e => e.Id == execution.Id);
        stored.StageDefinitionId.Should().Be(newStage.Id, "history is never rewritten by a master-data change");

        var parked = await db.ProductionStageDefinitions.AsNoTracking().SingleAsync(s => s.Id == newStage.Id);
        parked.IsActive.Should().BeFalse();
        parked.Name.Should().NotBeNullOrWhiteSpace("the row survives, so the traceability join still resolves");
    }

    [Fact]
    public async Task TheRawAllocationGate_AppliesToAnOrderThatStartsAtANewStage()
    {
        using var db = NewDb();
        await SeedStartingStagesAsync(db);
        var newStage = await CreateStageAsync(db, "PS026", "تشكيل جديد", 26);
        await FormationSetter(db).Handle(new SetFormationStageCommand(newStage.Id), CancellationToken.None);

        var orderId = await CreateOrderAsync(db);
        var order = await db.ProductionOrders.SingleAsync(o => o.Id == orderId);

        // H-1: the raw-allocation gate applies to an order that started at a stage configured in the
        // ERP exactly as it does to a seeded one. Phase 4: the gate is checked on the surviving
        // production paths (the transfer/completion transactions all call the same guard), so the
        // guard itself is exercised here directly and through the transfer path elsewhere.
        Func<Task> withoutAllocation = () => RawAllocationGuard.EnsureCompleteAsync(db, order, CancellationToken.None);
        (await withoutAllocation.Should().ThrowAsync<DomainException>())
            .WithMessage("*not fully allocated*");
        db.RawAllocations.Add(order.AllocateRaw(
            Guid.NewGuid(), order.ItemId, order.RequestedQuantityKg, order.RequestedQuantityMeter, "tester"));
        await db.SaveChangesAsync();

        Func<Task> withFullAllocation = () => RawAllocationGuard.EnsureCompleteAsync(db, order, CancellationToken.None);

        await withFullAllocation.Should().NotThrowAsync("100% of the requested quantity is now allocated");
    }
}
