using DyeHouseERP.Application.FormationRequests.Commands;
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
/// A Formation Request is an INDEPENDENT document: it never creates, changes or cancels a Job Order and
/// never posts stock. Decoupling it from Production Orders removed the Job-Order-driven sync that used to
/// advance its status, so the request must be able to record its OWN production directly. These tests pin
/// the whole lifecycle - Approved -&gt; In Progress -&gt; Partially Completed -&gt; Completed -&gt; through the
/// real command handlers, and prove that doing so touches no stock ledger and creates no Production Order.
/// </summary>
public class FormationProductionCommandTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public FormationProductionCommandTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"formation-production-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static StubCurrentUser User() => new("operator");

    /// <summary>A request with ONE group that has no basins, taken to Approved.</summary>
    private static async Task<(FormationRequest Request, FormationGroup Group)> SeedApprovedGroupAsync(
        ApplicationDbContext db, decimal planned = 500m)
    {
        var request = new FormationRequest("FRM-1", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: planned);
        var group = request.AddGroup("Cell A", planned, UnitOfMeasure.KG, 6, null, null, "tester");
        request.Submit("tester");
        request.Approve("manager");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();
        return (request, group);
    }

    /// <summary>A request with ONE group measured per basin (500 + 750 + 620 + 400 = 2270 KG), taken to Approved.</summary>
    private static async Task<(FormationRequest Request, FormationGroup Group, FormationBasin FirstBasin)>
        SeedApprovedBasinAsync(ApplicationDbContext db)
    {
        var request = new FormationRequest("FRM-2", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: 2270m);
        var group = request.AddGroup("Cell A", 2270m, UnitOfMeasure.KG, 4, null, null, "tester");
        var first = request.AddBasin(group.Id, "Basin 1", 500m, UnitOfMeasure.KG, 1, null, null, "tester");
        request.AddBasin(group.Id, "Basin 2", 750m, UnitOfMeasure.KG, 1, null, null, "tester");
        request.AddBasin(group.Id, "Basin 3", 620m, UnitOfMeasure.KG, 1, null, null, "tester");
        request.AddBasin(group.Id, "Basin 4", 400m, UnitOfMeasure.KG, 1, null, null, "tester");
        request.UpdateHeader(DateTime.UtcNow, request.RawMessageId, null,
            request.Groups.Sum(g => g.PlannedQuantity), "tester");
        request.Submit("tester");
        request.Approve("manager");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();
        return (request, group, first);
    }

    // --------------------------------------------------------------------- start production

    [Fact]
    public async Task StartProduction_OnAnApprovedRequest_MovesItToInProgress()
    {
        using var db = NewDb();
        var (request, _) = await SeedApprovedGroupAsync(db);

        var result = await new StartFormationProductionCommandHandler(db, User())
            .Handle(new StartFormationProductionCommand(request.Id), CancellationToken.None);

        result.Status.Should().Be(FormationRequestStatus.InProgress);
        (await db.FormationRequests.SingleAsync()).Status.Should().Be(FormationRequestStatus.InProgress);
    }

    [Fact]
    public async Task StartProduction_OnADraftRequest_IsRefused()
    {
        using var db = NewDb();
        var request = new FormationRequest("FRM-D", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: 100m);
        request.AddGroup("A", 100m, UnitOfMeasure.KG, 1, null, null, "tester");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();

        var act = () => new StartFormationProductionCommandHandler(db, User())
            .Handle(new StartFormationProductionCommand(request.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.FormationRequests.SingleAsync()).Status.Should().Be(FormationRequestStatus.Draft,
            "a refused transition leaves the status exactly as it was");
    }

    // --------------------------------------------------------------- record group production

    [Fact]
    public async Task RecordGroupProduction_Accumulates_AndDrivesTheCompletionStatus()
    {
        using var db = NewDb();
        var (request, group) = await SeedApprovedGroupAsync(db);

        var partial = await new RecordFormationGroupProductionCommandHandler(db, User())
            .Handle(new RecordFormationGroupProductionCommand(request.Id, group.Id, 200m), CancellationToken.None);
        partial.Status.Should().Be(FormationRequestStatus.PartiallyCompleted);
        partial.ProducedQuantity.Should().Be(200m);

        var complete = await new RecordFormationGroupProductionCommandHandler(db, User())
            .Handle(new RecordFormationGroupProductionCommand(request.Id, group.Id, 300m), CancellationToken.None);
        complete.Status.Should().Be(FormationRequestStatus.Completed);
        complete.ProducedQuantity.Should().Be(500m, "quantities accumulate - the same output is never double counted");
        complete.Groups.Single().RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task RecordGroupProduction_OnAGroupMeasuredPerBasin_IsRefused()
    {
        using var db = NewDb();
        var (request, group, _) = await SeedApprovedBasinAsync(db);

        // Production for a basin-measured cell must be recorded per basin, so the same physical
        // quantity can never be counted once against the group and again against its basins.
        var act = () => new RecordFormationGroupProductionCommandHandler(db, User())
            .Handle(new RecordFormationGroupProductionCommand(request.Id, group.Id, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.FormationGroups.SingleAsync()).ProducedQuantity.Should().Be(0m);
    }

    // -------------------------------------------------------------- record basin production

    [Fact]
    public async Task RecordBasinProduction_DerivesTheGroupFromItsBasins()
    {
        using var db = NewDb();
        var (request, group, first) = await SeedApprovedBasinAsync(db);

        var result = await new RecordFormationBasinProductionCommandHandler(db, User())
            .Handle(new RecordFormationBasinProductionCommand(request.Id, group.Id, first.Id, 500m),
                CancellationToken.None);

        var groupDto = result.Groups.Single();
        groupDto.ProducedQuantity.Should().Be(500m, "the group's figure is re-derived from its basins");
        groupDto.Basins.Single(b => b.Id == first.Id).ProducedQuantity.Should().Be(500m);
        groupDto.Basins.Where(b => b.Id != first.Id).Should().OnlyContain(b => b.ProducedQuantity == 0m);
        result.Status.Should().Be(FormationRequestStatus.PartiallyCompleted);
    }

    [Fact]
    public async Task RecordBasinProduction_ForABasinThatIsNotInTheGroup_IsRefused()
    {
        using var db = NewDb();
        var (request, group, _) = await SeedApprovedBasinAsync(db);

        var act = () => new RecordFormationBasinProductionCommandHandler(db, User())
            .Handle(new RecordFormationBasinProductionCommand(request.Id, group.Id, Guid.NewGuid(), 100m),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.FormationGroups.SingleAsync()).ProducedQuantity.Should().Be(0m,
            "a refused recording writes nothing");
    }

    [Fact]
    public async Task RecordProduction_OnADraftRequest_IsRefused()
    {
        using var db = NewDb();
        var request = new FormationRequest("FRM-D2", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(),
            UnitOfMeasure.KG, "tester", rawMessageId: Guid.NewGuid(), totalQuantity: 100m);
        var group = request.AddGroup("A", 100m, UnitOfMeasure.KG, 1, null, null, "tester");
        db.FormationRequests.Add(request);
        await db.SaveChangesAsync();

        var act = () => new RecordFormationGroupProductionCommandHandler(db, User())
            .Handle(new RecordFormationGroupProductionCommand(request.Id, group.Id, 50m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await db.FormationGroups.SingleAsync()).ProducedQuantity.Should().Be(0m);
    }

    // ------------------------------------------------- independence: no stock, no Job Order

    [Fact]
    public async Task RecordingProduction_TouchesNoStockLedgerAndCreatesNoProductionOrder()
    {
        using var db = NewDb();
        var (request, group) = await SeedApprovedGroupAsync(db);

        await new StartFormationProductionCommandHandler(db, User())
            .Handle(new StartFormationProductionCommand(request.Id), CancellationToken.None);
        await new RecordFormationGroupProductionCommandHandler(db, User())
            .Handle(new RecordFormationGroupProductionCommand(request.Id, group.Id, 500m), CancellationToken.None);

        (await db.ProductionOrders.CountAsync()).Should().Be(0, "a Formation Request never creates a Job Order");
        (await db.MaterialTransactions.CountAsync()).Should().Be(0, "and never posts a material movement");
        (await db.InventoryTransactions.CountAsync()).Should().Be(0, "nor a raw/ready stock movement");
    }

    // ------------------------------------------------------------------------- validators

    [Fact]
    public void ProductionValidators_RequireAPositiveQuantityAndTheIdSlots()
    {
        var groupValidator = new RecordFormationGroupProductionCommandValidator();
        var basinValidator = new RecordFormationBasinProductionCommandValidator();

        groupValidator.Validate(new RecordFormationGroupProductionCommand(Guid.NewGuid(), Guid.NewGuid(), 0m)).IsValid
            .Should().BeFalse("a produced quantity of zero is not production");
        groupValidator.Validate(new RecordFormationGroupProductionCommand(Guid.Empty, Guid.NewGuid(), 5m)).IsValid
            .Should().BeFalse();
        groupValidator.Validate(new RecordFormationGroupProductionCommand(Guid.NewGuid(), Guid.NewGuid(), 5m)).IsValid
            .Should().BeTrue();

        basinValidator.Validate(new RecordFormationBasinProductionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 5m)).IsValid
            .Should().BeFalse("recording production against a basin requires the basin");
        basinValidator.Validate(new RecordFormationBasinProductionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5m)).IsValid
            .Should().BeTrue();
    }
}
