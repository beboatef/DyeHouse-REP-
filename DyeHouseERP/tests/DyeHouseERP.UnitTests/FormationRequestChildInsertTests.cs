using DyeHouseERP.Application.FormationRequests.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// H-2a / H-2b regression cover. A group or a basin APPENDED to a formation request that already exists must be
/// INSERTed. Before the fix EF change detection took the new child (its key is generated client-side) for an
/// existing row and saved an UPDATE that hit nothing, so SaveChangesAsync threw DbUpdateConcurrencyException -
/// the user saw HTTP 409 and the whole edit was lost.
///
/// These are handler-level tests on purpose: they drive the real command handlers against a real DbContext,
/// because change-tracker state is invisible to a pure domain test - which is exactly why the defect survived
/// the existing suite.
/// </summary>
public class FormationRequestChildInsertTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public FormationRequestChildInsertTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"formation-child-insert-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>One group with one basin, created through the real create handler so it is genuinely persisted.</summary>
    private static async Task<(Guid RequestId, Guid GroupId, Guid BasinId)> SeedPersistedRequestAsync(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "tester");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "tester");
        db.AddRange(customer, item);
        await db.SaveChangesAsync();

        await new CreateFormationRequestCommandHandler(db, new StubCurrentUser("tester"), new FixedNumberGenerator())
            .Handle(new CreateFormationRequestCommand(customer.Id, item.Id, DateTime.UtcNow, UnitOfMeasure.KG,
                new List<FormationGroupInput>
                {
                    new(1000m, UnitOfMeasure.KG, TubCount: 1, Name: "G1",
                        Basins: new List<FormationBasinInput> { new(1000m, UnitOfMeasure.KG, Name: "B1") })
                }), CancellationToken.None);

        var request = await db.FormationRequests.Include(r => r.Groups).ThenInclude(g => g.Basins).SingleAsync();
        var group = request.Groups.Single();
        return (request.Id, group.Id, group.Basins.Single().Id);
    }

    private static UpdateFormationRequestCommandHandler Handler(ApplicationDbContext db)
        => new(db, new StubCurrentUser("tester"));

    [Fact]
    public async Task CreatingARequestWithGroupsAndBasins_StillInsertsTheWholeGraph()
    {
        using var db = NewDb();
        var (requestId, groupId, _) = await SeedPersistedRequestAsync(db);

        (await db.FormationGroups.CountAsync(g => g.FormationRequestId == requestId)).Should().Be(1);
        (await db.FormationBasins.CountAsync(b => b.FormationGroupId == groupId)).Should().Be(1);
    }

    [Fact]
    public async Task AddingANewGroupToAPersistedRequest_InsertsItAndKeepsTheExistingGroupInPlace()
    {
        using var db = NewDb();
        var (requestId, groupId, _) = await SeedPersistedRequestAsync(db);

        await Handler(db).Handle(new UpdateFormationRequestCommand(requestId, DateTime.UtcNow, UnitOfMeasure.KG,
            new List<FormationGroupInput>
            {
                new(1000m, UnitOfMeasure.KG, TubCount: 1, Name: "G1", Id: groupId),
                new(500m, UnitOfMeasure.KG, Name: "G2")
            }), CancellationToken.None);

        var groups = await db.FormationGroups.Where(g => g.FormationRequestId == requestId)
            .OrderBy(g => g.GroupNumber).ToListAsync();

        groups.Should().HaveCount(2, "the new group must be inserted - it used to be lost with the whole edit");
        groups.Select(g => g.Id).Should().Contain(groupId, "an existing group keeps its identity");
        groups.Select(g => g.Name).Should().Equal("G1", "G2");
        (await db.FormationRequests.SingleAsync(r => r.Id == requestId)).TotalQuantity
            .Should().Be(1500m, "the header total always mirrors the groups");
    }

    [Fact]
    public async Task AddingANewBasinToAPersistedGroup_InsertsItAndRestatesTheGroupQuantity()
    {
        using var db = NewDb();
        var (requestId, groupId, basinId) = await SeedPersistedRequestAsync(db);

        await Handler(db).Handle(new UpdateFormationRequestCommand(requestId, DateTime.UtcNow, UnitOfMeasure.KG,
            new List<FormationGroupInput>
            {
                new(1400m, UnitOfMeasure.KG, TubCount: 1, Name: "G1", Id: groupId,
                    Basins: new List<FormationBasinInput>
                    {
                        new(1000m, UnitOfMeasure.KG, Name: "B1", Id: basinId),
                        new(400m, UnitOfMeasure.KG, Name: "B2")
                    })
            }), CancellationToken.None);

        var basins = await db.FormationBasins.Where(b => b.FormationGroupId == groupId)
            .OrderBy(b => b.BasinNumber).ToListAsync();

        basins.Should().HaveCount(2, "the new basin must be inserted next to the existing one");
        basins.Select(b => b.Name).Should().Equal("B1", "B2");
        basins.Select(b => b.Id).Should().Contain(basinId, "the existing basin keeps its identity");

        (await db.FormationGroups.SingleAsync(g => g.Id == groupId)).PlannedQuantity
            .Should().Be(1400m, "a group with basins carries the sum of its basins");
    }

    [Fact]
    public async Task AddingGroupsInTwoSeparateEdits_InsertsEachGroupExactlyOnce()
    {
        using var db = NewDb();
        var (requestId, groupId, _) = await SeedPersistedRequestAsync(db);

        await Handler(db).Handle(new UpdateFormationRequestCommand(requestId, DateTime.UtcNow, UnitOfMeasure.KG,
            new List<FormationGroupInput>
            {
                new(1000m, UnitOfMeasure.KG, Name: "G1", Id: groupId),
                new(500m, UnitOfMeasure.KG, Name: "G2")
            }), CancellationToken.None);

        var afterFirst = await db.FormationGroups.Where(g => g.FormationRequestId == requestId).ToListAsync();
        afterFirst.Should().HaveCount(2);
        var g2 = afterFirst.Single(g => g.Name == "G2");

        // Second edit: keep both existing groups, add one more.
        await Handler(db).Handle(new UpdateFormationRequestCommand(requestId, DateTime.UtcNow, UnitOfMeasure.KG,
            new List<FormationGroupInput>
            {
                new(1000m, UnitOfMeasure.KG, Name: "G1", Id: groupId),
                new(500m, UnitOfMeasure.KG, Name: "G2", Id: g2.Id),
                new(250m, UnitOfMeasure.KG, Name: "G3")
            }), CancellationToken.None);

        var groups = await db.FormationGroups.Where(g => g.FormationRequestId == requestId)
            .OrderBy(g => g.GroupNumber).ToListAsync();

        groups.Should().HaveCount(3, "each edit inserts exactly its own new group - nothing is duplicated");
        groups.Select(g => g.Name).Should().Equal("G1", "G2", "G3");
        groups.Select(g => g.Id).Should().OnlyHaveUniqueItems();
        (await db.FormationRequests.SingleAsync(r => r.Id == requestId)).TotalQuantity.Should().Be(1750m);
    }

    [Fact]
    public async Task ReplacingTheGroupSetOfAPersistedRequest_InsertsTheNewGroupsAndDropsTheRemovedOne()
    {
        using var db = NewDb();
        var (requestId, groupId, _) = await SeedPersistedRequestAsync(db);

        await Handler(db).Handle(new UpdateFormationRequestCommand(requestId, DateTime.UtcNow, UnitOfMeasure.KG,
            new List<FormationGroupInput>
            {
                new(300m, UnitOfMeasure.KG, Name: "G9"),
                new(200m, UnitOfMeasure.KG, Name: "G10")
            }), CancellationToken.None);

        var groups = await db.FormationGroups.Where(g => g.FormationRequestId == requestId).ToListAsync();

        groups.Should().HaveCount(2);
        groups.Select(g => g.Name).Should().BeEquivalentTo(new[] { "G9", "G10" });
        groups.Select(g => g.Id).Should().NotContain(groupId, "a group dropped by the user is removed");
        (await db.FormationRequests.SingleAsync(r => r.Id == requestId)).TotalQuantity.Should().Be(500m);
    }
}
