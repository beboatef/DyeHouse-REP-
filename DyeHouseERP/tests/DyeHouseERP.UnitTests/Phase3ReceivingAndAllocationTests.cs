using System.Reflection;
using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.FormationRequests.Commands;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Application.RawReceipts.Commands;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 3 - receiving, formation requests and raw-material allocation.
///
/// What is pinned here, beyond what the existing suites already cover:
///   * receiving posts its IN movement ONCE, is idempotent against a retried confirmation,
///     and needs no inspection data at all;
///   * the receipt carries the phase's field set (customer, item, colour where applicable,
///     quantities + units, warehouse, date, system number, optional bale count, notes);
///   * creating and editing a Formation Request moves NO stock (planned is not issued);
///   * the order reports required / allocated / remaining / allocation status, judged by the
///     SAME domain rule the server enforces before production may start;
///   * the negative-stock exception on the issue path still refuses self-approval;
///   * the stock-lock service really receives its DbContext (without it every lock threw a
///     NullReferenceException before it could reach sp_getapplock - F.7).
/// </summary>
public class Phase3ReceivingAndAllocationTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public Phase3ReceivingAndAllocationTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase3-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private sealed class FakeUser : ICurrentUserService
    {
        private readonly HashSet<string> _permissions;
        public FakeUser(string userName, params string[] permissions)
        {
            UserName = userName;
            _permissions = permissions.ToHashSet(StringComparer.Ordinal);
        }
        public string UserName { get; }
        public Guid? UserId => null;
        public string? IpAddress => null;
        public bool IsInRole(string role) => _permissions.Contains(role);
        public bool HasPermission(string permission) =>
            _permissions.Contains(permission) || _permissions.Contains(Permissions.Admin);
    }

    // =====================================================================
    // 1. Raw-material receiving
    // =====================================================================

    private sealed class Scene
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required RawMessage Message { get; init; }
    }

    /// <summary>An item that measures in BOTH units (KG primary, Meter secondary), as fabric does.</summary>
    private static Item TwoUnitItem() => new("IT-1", "Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);

    /// <summary>One receipt of 1,200 KG / 3,000 Meter that nothing has touched - the starting point for receiving tests.</summary>
    private static Scene SeedReceipt(ApplicationDbContext db, decimal kg = 1200m, decimal meter = 3000m)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = TwoUnitItem();
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, kg, meter, 12, "seed bale count");

        db.AddRange(customer, item, warehouse, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: kg, quantityMeter: meter, direction: TransactionDirection.In, createdBy: "seed"));
        db.SaveChanges();

        return new Scene { Customer = customer, Item = item, Warehouse = warehouse, Message = message };
    }

    private static CreateRawMessageCommandHandler ReceivingHandler(ApplicationDbContext db,
        ICurrentUserService? user = null, IDocumentNumberGenerator? numbers = null) =>
        new(db, user ?? new StubCurrentUser("operator"), numbers ?? new FixedNumberGenerator(),
            new FixedClock(), new OpenPeriod(), new RecordingStockLock(), new InventoryLedgerService(db));

    private static CreateRawMessageCommand ReceiptOf(Scene scene, Guid? requestId = null, string? color = "Navy") =>
        new(DateTime.UtcNow, scene.Customer.Id, scene.Warehouse.Id, "received at the gate",
            new List<RawMessageLineInput>
            {
                new() { ItemId = scene.Item.Id, QuantityKg = 500m, QuantityMeter = 1250m, PieceCount = 25, Color = color, Notes = "rolls" }
            },
            requestId);

    [Fact]
    public async Task Receiving_With_No_Inspection_Data_At_All_Posts_The_Receipt_And_Its_In_Movement()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);
        var before = await db.InventoryTransactions.CountAsync();

        // No inspection result, no rejected quantity, no inspector - inspection is recorded
        // information, never a precondition for receiving (spec sections 8-9).
        var dto = await ReceivingHandler(db).Handle(ReceiptOf(scene), CancellationToken.None);

        var message = await db.RawMessages.SingleAsync(m => m.Id == dto.Id);
        message.InspectionStatus.Should().Be(InspectionStatus.PendingInspection);
        message.Inspector.Should().BeNull();
        message.InspectionDate.Should().BeNull();
        message.Status.Should().Be(RawMessageStatus.Open);
        message.IsAvailableForAllocation.Should().BeTrue("a pending inspection never blocks allocation");

        // The phase's field set, on the receipt that was actually stored.
        message.MessageNumber.Should().Be($"{DocumentType.RawReceiptMessage}-TEST-1",
            "the receiving document number is always produced by the numbering engine, never typed");
        message.CustomerId.Should().Be(scene.Customer.Id, "ownership is explicit on every receipt");
        message.WarehouseId.Should().Be(scene.Warehouse.Id);
        var line = message.Lines.Should().ContainSingle().Subject;
        line.ItemId.Should().Be(scene.Item.Id);
        line.QuantityKg.Should().Be(500m);
        line.QuantityMeter.Should().Be(1250m);
        line.PieceCount.Should().Be(25, "the bale/roll count is auxiliary data, not an inventory unit");
        line.Color.Should().Be("Navy", "colour is captured where applicable");
        line.Notes.Should().Be("rolls");

        // One IN ledger row per measured unit - the standard inventory workflow, not a balance edit.
        (await db.InventoryTransactions.CountAsync()).Should().Be(before + 1);
        var row = await db.InventoryTransactions.SingleAsync(t => t.SourceDocumentId == message.Id
            && t.SourceDocumentType == DocumentType.RawReceiptMessage);
        row.Direction.Should().Be(TransactionDirection.In);
        row.QuantityKg.Should().Be(500m);
        row.QuantityMeter.Should().Be(1250m);
        row.CustomerId.Should().Be(scene.Customer.Id);
        row.RawMessageId.Should().Be(message.Id, "the movement is traceable back to the receipt");

        // The returned DTO carries the stored facts and the live balance.
        dto.Lines.Single().Color.Should().Be("Navy", "the colour travels back to the caller");
        dto.Lines.Single().RemainingKg.Should().Be(500m);
    }

    [Fact]
    public async Task A_Retried_Receiving_Confirmation_With_The_Same_Token_Posts_Exactly_One_Receipt_And_One_Movement()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);
        var before = await db.InventoryTransactions.CountAsync();
        var token = Guid.NewGuid();
        var command = ReceiptOf(scene, token);

        var first = await ReceivingHandler(db).Handle(command, CancellationToken.None);
        var retry = await ReceivingHandler(db).Handle(command, CancellationToken.None);

        retry.Id.Should().Be(first.Id, "a retry returns the receipt that was already posted");
        retry.MessageNumber.Should().Be(first.MessageNumber);

        (await db.RawMessages.CountAsync()).Should().Be(2, "the seeded lot plus exactly ONE new receipt");
        (await db.InventoryTransactions.CountAsync()).Should().Be(before + 1,
            "the physical delivery must never be written into stock twice");
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.RawReceiptMessage
            && t.SourceDocumentId == first.Id)).Should().Be(1);

        // A DIFFERENT confirmation is a different delivery and is posted normally.
        var second = await ReceivingHandler(db).Handle(ReceiptOf(scene, Guid.NewGuid()), CancellationToken.None);
        second.Id.Should().NotBe(first.Id);
        (await db.RawMessages.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task A_Retried_Receipt_Reports_The_Live_Balance_Not_The_Originally_Received_Quantity()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);
        var token = Guid.NewGuid();
        var command = ReceiptOf(scene, token);

        var posted = await ReceivingHandler(db).Handle(command, CancellationToken.None);

        // 200 KG of the new lot is issued to production (an OUT row on the same lot/customer/item).
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.ProductionOrder, "PRD-1", Guid.NewGuid(), DateTime.UtcNow,
            scene.Warehouse.Id, scene.Customer.Id, scene.Item.Id, rawMessageId: posted.Id, productionOrderId: null,
            quantityKg: 200m, quantityMeter: null, direction: TransactionDirection.Out, createdBy: "floor"));
        await db.SaveChangesAsync();

        var retry = await ReceivingHandler(db).Handle(command, CancellationToken.None);

        retry.Lines.Single().RemainingKg.Should().Be(300m,
            "the retry answers with the lot's balance read live from the ledger, never a stale copy");
    }

    [Fact]
    public async Task Receiving_Stores_No_Colour_When_None_Was_Given()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        var dto = await ReceivingHandler(db).Handle(ReceiptOf(scene, color: null), CancellationToken.None);

        (await db.RawMessages.Include(m => m.Lines).SingleAsync(m => m.Id == dto.Id))
            .Lines.Single().Color.Should().BeNull("colour is optional - a plain article is received without one");
        dto.Lines.Single().Color.Should().BeNull();
    }

    // =====================================================================
    // 2. Formation Requests never touch stock (planned is NOT issued)
    // =====================================================================

    private static CreateFormationRequestCommand FormationRequestOf(Scene scene) => new(
        scene.Customer.Id, scene.Item.Id, DateTime.UtcNow, UnitOfMeasure.KG,
        new List<FormationGroupInput>
        {
            new(PlannedQuantity: 600m, Unit: UnitOfMeasure.KG, TubCount: 6, Name: "Cell A", Color: "Navy",
                Basins: new List<FormationBasinInput>
                {
                    new(PlannedQuantity: 300m, Unit: UnitOfMeasure.KG, TubCount: 3, Name: "Basin 1", Color: "Navy"),
                    new(PlannedQuantity: 300m, Unit: UnitOfMeasure.KG, TubCount: 3, Name: "Basin 2", Color: "Navy")
                }),
            new(PlannedQuantity: 400m, Unit: UnitOfMeasure.KG, TubCount: 4, Name: "Cell B", Color: "Red")
        },
        RawMessageId: scene.Message.Id, Notes: "customer's planning sheet");

    [Fact]
    public async Task Creating_And_Editing_A_Formation_Request_Moves_No_Stock_At_All()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        var ledgerBefore = await db.InventoryTransactions.CountAsync();
        var balanceBefore = (await new InventoryLedgerService(db).GetCustomerBalanceAsync(
            scene.Message.Id, scene.Item.Id, scene.Customer.Id, scene.Warehouse.Id, CancellationToken.None));

        var user = new StubCurrentUser("planner");
        var created = await new CreateFormationRequestCommandHandler(db, user, new FixedNumberGenerator())
            .Handle(FormationRequestOf(scene), CancellationToken.None);

        created.Groups.Should().HaveCount(2);
        created.TotalQuantity.Should().Be(1000m);
        created.BasinCount.Should().Be(2, "a group may be planned as several basins");

        // ---- the same request is then edited (quantities, a basin removed, a basin added) ----
        var updated = await new UpdateFormationRequestCommandHandler(db, user)
            .Handle(new UpdateFormationRequestCommand(
                created.Id, DateTime.UtcNow, UnitOfMeasure.KG,
                new List<FormationGroupInput>
                {
                    new(PlannedQuantity: 700m, Unit: UnitOfMeasure.KG, TubCount: 7, Name: "Cell A", Color: "Navy",
                        Basins: new List<FormationBasinInput>
                        {
                            new(PlannedQuantity: 350m, Unit: UnitOfMeasure.KG, TubCount: 3, Name: "Basin 1", Color: "Navy",
                                Id: created.Groups.First(g => g.Name == "Cell A").Basins.First(b => b.Name == "Basin 1").Id),
                            new(PlannedQuantity: 350m, Unit: UnitOfMeasure.KG, TubCount: 4, Name: "Basin 3", Color: "Navy")
                        },
                        Id: created.Groups.First(g => g.Name == "Cell A").Id),
                    new(PlannedQuantity: 500m, Unit: UnitOfMeasure.KG, TubCount: 5, Name: "Cell B", Color: "Red",
                        Id: created.Groups.First(g => g.Name == "Cell B").Id)
                },
                RawMessageId: scene.Message.Id, Notes: "revised planning sheet"),
            CancellationToken.None);

        updated.TotalQuantity.Should().Be(1200m);
        updated.Groups.SelectMany(g => g.Basins).Select(b => b.Name).Should().Contain(new[] { "Basin 1", "Basin 3" })
            .And.NotContain("Basin 2");

        // ---- and the whole exercise is plan-only: no ledger row, allocation, adjustment or
        //      override was written, and the customer's lot still holds exactly what it did ----
        (await db.InventoryTransactions.CountAsync()).Should().Be(ledgerBefore,
            "a Formation Request is planning only - it never creates or changes stock");
        (await db.RawAllocations.CountAsync()).Should().Be(0);
        (await db.StockAdjustments.CountAsync()).Should().Be(0);
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(0);
        (await db.ProductionOrders.CountAsync()).Should().Be(0,
            "a Formation Request is not a prerequisite for - and cannot itself create - a Job Order");

        var balanceAfter = await new InventoryLedgerService(db).GetCustomerBalanceAsync(
            scene.Message.Id, scene.Item.Id, scene.Customer.Id, scene.Warehouse.Id, CancellationToken.None);
        balanceAfter.Should().Be(balanceBefore, "planned quantities are never issued");
    }

    // =====================================================================
    // 3. Required / allocated / remaining / allocation status
    // =====================================================================

    [Fact]
    public async Task The_Order_Reports_Required_Allocated_Remaining_And_Status_By_The_Rule_The_Server_Enforces()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        // A legacy (issue-less) order for 1,000 KG asks for more than the seeded lot holds, so the
        // allocation is deliberately PARTIAL: this is exactly the "<100% must not start" case.
        var order = new ProductionOrder("PRD-1", scene.Customer.Id, scene.Item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 1000m);
        db.ProductionOrders.Add(order);
        db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, 400m, null, "seed"));
        await db.SaveChangesAsync();

        var dto = await new GetProductionOrderByIdQueryHandler(db)
            .Handle(new GetProductionOrderByIdQuery(order.Id), CancellationToken.None);

        dto.RequestedQuantityKg.Should().Be(1000m, "required is the order's own requested quantity");
        dto.AllocatedQuantityKg.Should().Be(400m);
        dto.RemainingToAllocateKg.Should().Be(600m);
        dto.IsRawAllocationComplete.Should().BeFalse("a partial allocation may not start production");

        // The displayed status IS the enforcement: the very same rule the start/transfer/complete
        // paths run through RawAllocationGuard refuses this order.
        var orderNow = await db.ProductionOrders.SingleAsync(o => o.Id == order.Id);
        var refused = () => orderNow.EnsureRawAllocationComplete(400m, 0m);
        refused.Should().Throw<DomainException>().WithMessage("*not fully allocated*");

        // Completing the allocation flips both the display and the enforcement together.
        db.RawAllocations.Add(orderNow.AllocateRaw(scene.Message.Id, scene.Item.Id, 600m, null, "seed"));
        await db.SaveChangesAsync();

        var completed = await new GetProductionOrderByIdQueryHandler(db)
            .Handle(new GetProductionOrderByIdQuery(order.Id), CancellationToken.None);
        completed.AllocatedQuantityKg.Should().Be(1000m);
        completed.RemainingToAllocateKg.Should().Be(0m, "remaining is never reported as a negative quantity");
        completed.IsRawAllocationComplete.Should().BeTrue();

        var accepted = () => orderNow.EnsureRawAllocationComplete(1000m, 0m);
        accepted.Should().NotThrow("what the screen says is allowed is what the server allows");
    }

    [Fact]
    public async Task An_Over_Allocated_Order_Reports_Zero_Remaining_And_Not_A_Negative_Figure()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);

        var order = new ProductionOrder("PRD-1", scene.Customer.Id, scene.Item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 100m);
        db.ProductionOrders.Add(order);
        db.RawAllocations.Add(order.AllocateRaw(scene.Message.Id, scene.Item.Id, 150m, null, "seed"));
        await db.SaveChangesAsync();

        var dto = await new GetProductionOrderByIdQueryHandler(db)
            .Handle(new GetProductionOrderByIdQuery(order.Id), CancellationToken.None);

        dto.AllocatedQuantityKg.Should().Be(150m);
        dto.RemainingToAllocateKg.Should().Be(0m);
        dto.IsRawAllocationComplete.Should().BeTrue();
    }

    // =====================================================================
    // 4. Negative-stock exception on the ISSUE path: separation of duties
    // =====================================================================

    private static IssueRawMaterialCommand IssueOf(Scene scene, Guid? approvalId = null) => new(
        scene.Customer.Id, scene.Item.Id, scene.Message.Id, DateTime.UtcNow,
        ActualQuantityKg: 5000m, ActualQuantityMeter: null,
        OverrideNegativeStock: true, OverrideReason: "lot is short by 3,800 KG", ApprovalRequestId: approvalId);

    [Fact]
    public async Task The_Requester_Of_A_Negative_Stock_Exception_Cannot_Approve_Their_Own_Issue()
    {
        using var db = NewDb();
        var scene = SeedReceipt(db);
        db.ProductionStageDefinitions.Add(new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true));
        var seeded = new User("operator", "hash", "Operator", Permissions.Admin, "seed");
        db.Users.Add(seeded);
        await db.SaveChangesAsync();

        // The same account both raises the request and tries to execute it with its own id.
        var requester = new FakeUser("operator", Permissions.RawConsume,
            Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);

        var submitted = await new SubmitApprovalRequestCommandHandler(db, requester, new FixedClock())
            .Handle(new SubmitApprovalRequestCommand(ApprovalRequestType.RawIssue, "lot is short by 3,800 KG",
                ApprovalPayload.Serialize(IssueOf(scene))), CancellationToken.None);

        var act = () => new IssueRawMaterialCommandHandler(
                db, requester, new InventoryLedgerService(db), new FixedNumberGenerator(), new FixedClock(),
                new OpenPeriod(), new RecordingStockLock())
            .Handle(IssueOf(scene, submitted.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*cannot also approve it*");

        (await db.ProductionOrders.CountAsync()).Should().Be(0, "a refused override creates no Job Order");
        (await db.RawAllocations.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.ProductionOrder))
            .Should().Be(0, "and posts no issue movement");
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    // =====================================================================
    // 5. The stock lock must actually be able to reach the database (F.7)
    // =====================================================================

    [Fact]
    public void The_Stock_Lock_Service_Is_Constructed_With_Its_DbContext()
    {
        // Regression guard for F.7: the service used to declare the context as a readonly
        // field with NO constructor, so DI handed it null and every stock lock threw a
        // NullReferenceException before it could take sp_getapplock. The whole "concurrent
        // postings serialize on the same lot" guarantee was therefore live only in unit tests,
        // where a recording lock double is substituted for this class.
        var constructors = typeof(SqlAllocationLockService).GetConstructors();

        constructors.Should().ContainSingle("the lock service has exactly one way to be built")
            .Which.GetParameters().Select(p => p.ParameterType)
            .Should().Contain(typeof(ApplicationDbContext), "the lock is taken on the scoped context's own connection");

        using var db = NewDb();
        var service = (SqlAllocationLockService)Activator.CreateInstance(typeof(SqlAllocationLockService), db)!;
        var field = typeof(SqlAllocationLockService)
            .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!;

        field.GetValue(service).Should().BeSameAs(db,
            "a null context is exactly the defect this test exists to catch");
    }

    [Fact]
    public async Task Acquiring_No_Stock_Dimensions_Is_A_NoOp_And_Needs_No_Lock_At_All()
    {
        using var db = NewDb();
        var service = new SqlAllocationLockService(db);

        await using var guard = await service.AcquireManyAsync(Array.Empty<StockLockKey>(), CancellationToken.None);

        guard.Should().NotBeNull();
    }
}
