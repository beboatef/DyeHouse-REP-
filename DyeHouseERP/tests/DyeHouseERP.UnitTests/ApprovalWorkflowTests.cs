using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Approvals.Queries;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 24: separation of duties as a real, authenticated workflow.
///
/// The five paths that may take a balance negative (raw allocation, the
/// pause/resume path's re-issue, customer transfer, external processing and
/// stock adjustment) are no longer "type the approver's name":
///
///   * the requester SUBMITS what they want to do and why,
///   * a DIFFERENT, authenticated user approves or rejects it,
///   * the approver's identity always comes from their own session,
///   * approving executes the module's own command, so every module rule runs,
///   * the approval is consumed together with the posting (single use), and
///   * a rejected request records the reason and never executes.
/// </summary>
public class ApprovalWorkflowTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ApprovalWorkflowTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"approval-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>Permission-aware current-user fake (the shared stub grants everything).</summary>
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

    private sealed class Fixture
    {
        public required Customer Customer { get; init; }
        public required Item Item { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required RawMessage Message { get; init; }
        public required ProductionOrder Order { get; init; }
    }

    /// <summary>
    /// One message holding 100 KG for one customer and one order drawing from it,
    /// so a 500 KG allocation is genuinely short of balance (the situation an
    /// override approval exists for).
    /// </summary>
    private Fixture Seed(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, 100m, null, null, null);
        db.RawMessages.Add(message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 100m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));

        var order = new ProductionOrder("PRD-1", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 500m);
        db.ProductionOrders.Add(order);

        // The raw ISSUE is the canonical negative-stock path this fixture now exercises, so the route it
        // snapshots when it creates the order must exist: one explicit formation stage.
        db.ProductionStageDefinitions.Add(
            new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true));

        db.SaveChanges();

        return new Fixture { Customer = customer, Item = item, Warehouse = warehouse, Message = message, Order = order };
    }

    private static void SeedUsers(ApplicationDbContext db, params (string Username, string Roles, bool Active)[] users)
    {
        foreach (var (username, roles, active) in users)
        {
            var user = new User(username, "hash", username, roles, "seed");
            db.Users.Add(user);
            if (!active) user.Deactivate("seed");
        }
        db.SaveChanges();
    }

    /// <summary>
    /// The override being requested: a raw ISSUE of more material than the lot still holds.
    ///
    /// This used to be raised as an "allocate raw to an existing Job Order" request. That operation was
    /// removed (the raw deduction is made once, by the issue that creates the order), so the same
    /// separation-of-duties workflow is exercised through the issue that REPLACED it - the mechanism under
    /// test (submit -> a different user decides -> the module's own command runs -> the approval is consumed
    /// with the posting) is unchanged.
    /// </summary>
    private static SubmitApprovalRequestCommand SubmitFor(Fixture f, string quantityKg = "500")
        => new(
            ApprovalRequestType.RawIssue,
            "balance will go negative while the job runs",
            $$"""
            {
              "customerId": "{{f.Customer.Id}}",
              "itemId": "{{f.Item.Id}}",
              "rawMessageId": "{{f.Message.Id}}",
              "orderDate": "2026-03-01T00:00:00Z",
              "actualQuantityKg": {{quantityKg}},
              "overrideNegativeStock": true,
              "overrideReason": "stale client value that must be overwritten"
            }
            """);

    // ------------------------------------------------------------ submitting

    [Fact]
    public async Task Submitting_AnOverride_RequiresTheActionsOwnPermission()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("clerk", Permissions.InventoryView, true));
        var handler = new SubmitApprovalRequestCommandHandler(
            db, new FakeUser("clerk", Permissions.InventoryView), new FixedClock());

        var act = () => handler.Handle(SubmitFor(f), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage($"*{Permissions.RawConsume}*");
        (await db.ApprovalRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Submitting_StoresTheActionServerSide_AndRecordsTheRequester()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", Permissions.RawConsume, true));
        var handler = new SubmitApprovalRequestCommandHandler(
            db, new FakeUser("operator", Permissions.RawConsume), new FixedClock());

        var dto = await handler.Handle(SubmitFor(f), CancellationToken.None);

        dto.RequestedBy.Should().Be("operator", "the requester is the authenticated caller, not a body field");
        dto.Status.Should().Be(nameof(ApprovalRequestStatus.Pending));
        dto.DocumentNumber.Should().Be(f.Message.MessageNumber, "the subject's number is resolved server-side");
        dto.RequiredPermission.Should().Be(Permissions.InventoryApproveNegativeStock);
        dto.DecidedBy.Should().BeNull();

        var stored = await db.ApprovalRequests.SingleAsync();
        stored.Reason.Should().Be("balance will go negative while the job runs");

        // The stored action is normalized: always an override, carrying THIS
        // request's reason, and with no approver anywhere in it.
        var payload = stored.PayloadJson;
        payload.Should().Contain("\"overrideNegativeStock\":true")
            .And.NotContain("stale client value")
            .And.NotContain("approvedBy", "approver identity never travels in a payload");
        payload.Should().Contain("balance will go negative while the job runs");
    }

    [Fact]
    public async Task Submitting_TheSameSubjectTwice_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", Permissions.RawConsume, true));
        var handler = new SubmitApprovalRequestCommandHandler(
            db, new FakeUser("operator", Permissions.RawConsume), new FixedClock());

        await handler.Handle(SubmitFor(f), CancellationToken.None);
        var act = () => handler.Handle(SubmitFor(f), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*already exists*");
        (await db.ApprovalRequests.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Submitting_AnInvalidAction_IsRefused_ByTheModulesOwnValidator()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", Permissions.RawConsume, true));
        var handler = new SubmitApprovalRequestCommandHandler(
            db, new FakeUser("operator", Permissions.RawConsume), new FixedClock());

        // quantityKg 0 is not a valid allocation, so no approval may be raised
        // for it either - the module's validator is the single source of truth.
        var act = () => handler.Handle(SubmitFor(f, quantityKg: "0"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not valid*");
        (await db.ApprovalRequests.CountAsync()).Should().Be(0);
    }

    // --------------------------------------------------- the SoD guard itself

    /// <summary>
    /// A stand-in for a module operation, so the guard's own rules can be
    /// exercised without building a whole movement. The signature is what binds
    /// an approval to the exact facts, so it has to match the stored payload.
    /// </summary>
    // ------------------------------------------- payroll approval queue

    /// <summary>
    /// The centralized queue (spec section 44) lists a payroll run that is waiting for an approver: a Draft
    /// run (the simple path the domain documents) and a run that was calculated and SUBMITTED for review
    /// (UnderReview). A merely Calculated run is still editable and has not been submitted, so it is not listed.
    /// </summary>
    [Fact]
    public async Task PayrollRunsWaitingForApproval_AreListed_ButNotMerelyCalculatedOnes()
    {
        using var db = NewDb();
        var draft = new PayrollRun("PRL-2026-000101", 2026, 1, "hr");
        var submitted = new PayrollRun("PRL-2026-000102", 2026, 2, "hr");
        var calculatedOnly = new PayrollRun("PRL-2026-000103", 2026, 3, "hr");
        db.PayrollRuns.AddRange(draft, submitted, calculatedOnly);
        await db.SaveChangesAsync();

        submitted.MarkCalculated(new Dictionary<Guid, PayrollLineAmounts>(),
            new Dictionary<Guid, Guid?>(), new Dictionary<Guid, Guid?>(), "hr");
        submitted.SubmitForReview("hr");
        calculatedOnly.MarkCalculated(new Dictionary<Guid, PayrollLineAmounts>(),
            new Dictionary<Guid, Guid?>(), new Dictionary<Guid, Guid?>(), "hr");
        await db.SaveChangesAsync();

        var handler = new GetApprovalCenterQueryHandler(db, new FakeUser("manager", Permissions.PayrollApprove));
        var center = await handler.Handle(new GetApprovalCenterQuery(), CancellationToken.None);

        var payrollIds = center.Items.Where(i => i.Category == "PayrollRun").Select(i => i.Id).ToList();
        payrollIds.Should().Contain(draft.Id);
        payrollIds.Should().Contain(submitted.Id);
        payrollIds.Should().NotContain(calculatedOnly.Id, "a calculated run has not been submitted for review");
    }

    private sealed class StubOperation : IApprovalBoundOperation
    {
        public StubOperation(ApprovalRequestType type, Guid documentId, string signature = "stub-operation")
        {
            ApprovalType = type;
            ApprovalDocumentId = documentId;
            OperationSignature = signature;
        }

        public ApprovalRequestType ApprovalType { get; }
        public Guid ApprovalDocumentId { get; }
        public string OperationSignature { get; }
    }

    private static ApprovalRequest PendingRequest(
        ApplicationDbContext db, Fixture f, string requestedBy = "operator", string signature = "stub-operation")
    {
        var request = new ApprovalRequest(
            ApprovalRequestType.AllocateRaw, f.Order.Id, f.Order.OrderNumber,
            "needs to run today",
            ApprovalBinding.EmbedSignature("""{"x":1}""", signature),
            requestedBy, DateTime.UtcNow);
        db.ApprovalRequests.Add(request);
        db.SaveChanges();
        return request;
    }

    [Fact]
    public async Task AnOverride_WithoutAStoredApprovalRequest_IsRefused()
    {
        using var db = NewDb();
        Seed(db);

        var act = () => ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("someone", Permissions.InventoryApproveNegativeStock),
            null, new StubOperation(ApprovalRequestType.AllocateRaw, Guid.NewGuid()), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");
    }

    [Fact]
    public async Task TheRequester_CanNeverApproveTheirOwnRequest()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", Permissions.InventoryApproveNegativeStock, true));
        var request = PendingRequest(db, f, requestedBy: "operator");

        var act = () => ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("operator", Permissions.InventoryApproveNegativeStock),
            request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*cannot also approve it*");
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task AnApprover_WithoutTheApprovalPermission_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", "x", true), ("viewer", Permissions.InventoryView, true));
        var request = PendingRequest(db, f, requestedBy: "operator");

        var act = () => ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("viewer", Permissions.InventoryView),
            request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage($"*{Permissions.InventoryApproveNegativeStock}*");
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task AnInactiveApprover_CannotApprove()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", "x", true), ("fired", Permissions.InventoryApproveNegativeStock, false));
        var request = PendingRequest(db, f, requestedBy: "operator");

        var act = () => ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("fired", Permissions.InventoryApproveNegativeStock),
            request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*not an active account*");
    }

    [Fact]
    public async Task AnApproval_CannotBePlayedAgainstAnotherDocumentOrAction()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", "x", true), ("approver", Permissions.InventoryApproveNegativeStock, true));
        var request = PendingRequest(db, f, requestedBy: "operator");
        var currentUser = new FakeUser("approver", Permissions.InventoryApproveNegativeStock);

        var wrongDocument = () => ApprovalRequestGuard.ConsumeAsync(
            db, currentUser, request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, Guid.NewGuid()), CancellationToken.None);
        (await wrongDocument.Should().ThrowAsync<DomainException>())
            .WithMessage("*different document*");

        var wrongType = () => ApprovalRequestGuard.ConsumeAsync(
            db, currentUser, request.Id, new StubOperation(ApprovalRequestType.StockAdjustment, f.Order.Id), CancellationToken.None);
        (await wrongType.Should().ThrowAsync<DomainException>())
            .WithMessage("*was raised for*");

        // Same document, same action, DIFFERENT operation details (another
        // quantity / item / account / unit): the request was not approved for
        // that, so it cannot authorise it.
        var otherOperation = () => ApprovalRequestGuard.ConsumeAsync(
            db, currentUser, request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id, "some-other-facts"),
            CancellationToken.None);
        (await otherOperation.Should().ThrowAsync<DomainException>())
            .WithMessage("*different operation details*");

        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task AValidApproval_ConsumesTheRequest_AndNamesBothPeople()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", "x", true), ("floor-manager", Permissions.InventoryApproveNegativeStock, true));
        var request = PendingRequest(db, f, requestedBy: "operator");

        var decision = await ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("floor-manager", Permissions.InventoryApproveNegativeStock),
            request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id), CancellationToken.None);

        decision.RequestedBy.Should().Be("operator");
        decision.ApprovedBy.Should().Be("floor-manager", "the canonical account name, not a typed string");

        var consumed = await db.ApprovalRequests.SingleAsync();
        consumed.Status.Should().Be(ApprovalRequestStatus.Approved);
        consumed.DecidedBy.Should().Be("floor-manager");
        consumed.DecidedAtUtc.Should().NotBeNull();

        // Single use: a second decision on the same request is refused.
        var again = () => ApprovalRequestGuard.ConsumeAsync(
            db, new FakeUser("floor-manager", Permissions.InventoryApproveNegativeStock),
            request.Id, new StubOperation(ApprovalRequestType.AllocateRaw, f.Order.Id), CancellationToken.None);
        (await again.Should().ThrowAsync<DomainException>())
            .WithMessage("*already Approved*");
    }

    // ------------------------------------------------------------- rejecting

    [Fact]
    public async Task Rejecting_RequiresAReason_RecordsTheDecider_AndNeverExecutes()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", "x", true), ("floor-manager", Permissions.InventoryApproveNegativeStock, true));
        var request = PendingRequest(db, f, requestedBy: "operator");
        var decider = new FakeUser("floor-manager", Permissions.InventoryApproveNegativeStock);

        var noReason = () => new RejectApprovalRequestCommandHandler(db, decider, new FixedClock())
            .Handle(new RejectApprovalRequestCommand(request.Id, "   "), CancellationToken.None);
        (await noReason.Should().ThrowAsync<DomainException>())
            .WithMessage("*requires a reason*");

        var dto = await new RejectApprovalRequestCommandHandler(db, decider, new FixedClock())
            .Handle(new RejectApprovalRequestCommand(request.Id, "Use the other lot instead"), CancellationToken.None);

        dto.Status.Should().Be(nameof(ApprovalRequestStatus.Rejected));
        dto.DecidedBy.Should().Be("floor-manager");
        dto.DecisionReason.Should().Be("Use the other lot instead");

        // The requester may not "reject" their own request either - a decision is
        // always the other person's.
        var selfReject = () => new RejectApprovalRequestCommandHandler(
                db, new FakeUser("operator", Permissions.InventoryApproveNegativeStock), new FixedClock())
            .Handle(new RejectApprovalRequestCommand(request.Id, "changed my mind"), CancellationToken.None);
        (await selfReject.Should().ThrowAsync<DomainException>())
            .WithMessage("*already Rejected*", "the request is already decided, so nothing can be replayed");
    }

    // --------------------------------------------------- end-to-end approve

    /// <summary>A real MediatR pipeline so approval dispatch runs the actual module command.</summary>
    private static ISender BuildSender(ApplicationDbContext db, ICurrentUserService user)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IssueRawMaterialCommand>());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(user);
        services.AddSingleton<ICurrentUserService>(user);
        services.AddSingleton<IDateTime>(new FixedClock());
        services.AddSingleton<IPeriodCloseService>(new OpenPeriod());
        services.AddSingleton<IAllocationLockService>(new RecordingStockLock());
        services.AddSingleton<IInventoryLedgerService>(new DyeHouseERP.Persistence.Services.InventoryLedgerService(db));
        services.AddSingleton<IDocumentNumberGenerator>(new FixedNumberGenerator());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Approving_RunsTheModulesOwnCommand_AndRecordsBothUsersOnTheOverride()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db,
            ("operator", Permissions.RawConsume, true),
            ("floor-manager", Permissions.InventoryApproveNegativeStock + "," + Permissions.InventoryAllowNegativeStock, true));

        var submitted = await new SubmitApprovalRequestCommandHandler(
                db, new FakeUser("operator", Permissions.RawConsume), new FixedClock())
            .Handle(SubmitFor(f), CancellationToken.None);

        // The approver is a DIFFERENT, authenticated user with the approval
        // permission - their identity never appears in any request body.
        var approver = new FakeUser("floor-manager",
            Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);
        var dto = await BuildSender(db, approver)
            .Send(new ApproveApprovalRequestCommand(submitted.Id));

        dto.Status.Should().Be(nameof(ApprovalRequestStatus.Approved));
        dto.DecidedBy.Should().Be("floor-manager");

        // The movement actually executed through the module's own command...
        (await db.RawAllocations.CountAsync()).Should().Be(1);
        (await db.InventoryTransactions.CountAsync(t =>
            t.SourceDocumentType == DocumentType.ProductionOrder && t.Direction == TransactionDirection.Out))
            .Should().Be(1);

        // ...and the exception record names BOTH real accounts.
        var overrider = await db.NegativeStockOverrides.SingleAsync();
        overrider.RequestedBy.Should().Be("operator");
        overrider.ApprovedBy.Should().Be("floor-manager");

        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task Approving_YourOwnRequest_IsRefused_AndNothingIsPosted()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db, ("operator", Permissions.RawConsume + "," + Permissions.InventoryApproveNegativeStock, true));

        var submitted = await new SubmitApprovalRequestCommandHandler(
                db, new FakeUser("operator", Permissions.RawConsume), new FixedClock())
            .Handle(SubmitFor(f), CancellationToken.None);

        // Same account, holding every permission: the SoD rule still refuses.
        var self = new FakeUser("operator", Permissions.RawConsume,
            Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);

        var act = () => BuildSender(db, self).Send(new ApproveApprovalRequestCommand(submitted.Id));

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*cannot also approve it*");

        (await db.RawAllocations.CountAsync()).Should().Be(0, "a refused approval posts nothing");
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending,
            "the request stays open for a different user to decide");
    }

    [Fact]
    public async Task AnApprovedRequest_CannotBeReplayed()
    {
        using var db = NewDb();
        var f = Seed(db);
        SeedUsers(db,
            ("operator", Permissions.RawConsume, true),
            ("floor-manager", Permissions.InventoryApproveNegativeStock + "," + Permissions.InventoryAllowNegativeStock, true));

        var submitted = await new SubmitApprovalRequestCommandHandler(
                db, new FakeUser("operator", Permissions.RawConsume), new FixedClock())
            .Handle(SubmitFor(f), CancellationToken.None);

        var approver = new FakeUser("floor-manager",
            Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);
        var sender = BuildSender(db, approver);

        await sender.Send(new ApproveApprovalRequestCommand(submitted.Id));

        var replay = () => sender.Send(new ApproveApprovalRequestCommand(submitted.Id));
        (await replay.Should().ThrowAsync<DomainException>())
            .WithMessage("*already Approved*", "one approval authorises exactly one movement");

        (await db.RawAllocations.CountAsync()).Should().Be(1, "the replay must not post a second allocation");
    }

    [Fact]
    public void AnApprovalRequest_IsOnlyDecidableWhilePending()
    {
        using var db = NewDb();
        var f = Seed(db);
        var request = new ApprovalRequest(
            ApprovalRequestType.AllocateRaw, f.Order.Id, f.Order.OrderNumber,
            "reason", "{}", "operator", DateTime.UtcNow);

        request.Approve("floor-manager", DateTime.UtcNow);
        Action twice = () => request.Approve("someone-else", DateTime.UtcNow);
        twice.Should().Throw<DomainException>();

        Action rejectAfterApprove = () => request.Reject("someone-else", "no", DateTime.UtcNow);
        rejectAfterApprove.Should().Throw<DomainException>();

        var fresh = new ApprovalRequest(
            ApprovalRequestType.AllocateRaw, f.Order.Id, f.Order.OrderNumber,
            "reason", "{}", "operator", DateTime.UtcNow);
        Action rejectWithoutReason = () => fresh.Reject("floor-manager", "  ", DateTime.UtcNow);
        rejectWithoutReason.Should().Throw<DomainException>();
    }
}
