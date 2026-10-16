using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Approvals.DTOs;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.CustomerTransfers.Commands;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.RawExternalReleases.Commands;
using DyeHouseERP.Application.StockAdjustments.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec sections 17 + 24: the remaining negative-stock gaps.
///
/// Raw allocation already required a stored, single-use approval raised by a
/// different user. The customer transfer, the raw external release (external
/// processing) and the stock adjustment handlers did NOT: each of them let a
/// caller pass <c>overrideNegativeStock = true</c> with a typed reason, and
/// recorded that same caller as BOTH the requester and the approver on the
/// exception row - a self-approval with no approval workflow at all.
///
/// These tests pin the replacement rule for every one of those paths:
///
///   * negative stock is still blocked by default;
///   * the override only proceeds with a stored, PENDING approval request;
///   * the requester can never consume (approve) their own request;
///   * the approval is single-use;
///   * it is bound to the exact operation - item, account, both quantities
///     (unit), the operation's own discriminator and the subject document - so
///     changing any approved detail invalidates it;
///   * a valid approval executes the module's own command, posts the same ledger
///     rows as a normal movement, and records requester + approver + reason.
/// </summary>
public class NegativeStockApprovalGatingTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public NegativeStockApprovalGatingTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"negative-stock-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    /// <summary>Permission-aware current-user fake (mirrors the other approval tests).</summary>
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
        public required Customer From { get; init; }
        public required Customer To { get; init; }
        public required Item Item { get; init; }
        public required Item OtherItem { get; init; }
        public required Warehouse Warehouse { get; init; }
        public required RawMessage Message { get; init; }
        public required ProductionOrder Order { get; init; }
    }

    private const decimal MessageBalanceKg = 100m;

    /// <summary>One message holding 100 KG for the sending customer, so 150 KG is genuinely short.</summary>
    private Fixture Seed(ApplicationDbContext db)
    {
        var from = new Customer("C-1", "Sender", "seed");
        var to = new Customer("C-2", "Receiver", "seed");
        // The item uses both units (KG primary, Meter secondary) so a KG or a Meter movement
        // is genuinely allowed for it; OtherItem exists so a test can prove that changing the
        // approved ITEM invalidates the approval without also failing an unrelated item check.
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var otherItem = new Item("IT-2", "Other Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var warehouse = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");

        db.Customers.AddRange(from, to);
        db.Items.AddRange(item, otherItem);
        db.Warehouses.Add(warehouse);

        var message = new RawMessage("RMSG-1", DateTime.UtcNow, from.Id, warehouse.Id, "seed", "seed");
        message.AddLine(item.Id, MessageBalanceKg, null, null, null);
        db.RawMessages.Add(message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            warehouse.Id, from.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: MessageBalanceKg, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));

        var order = new ProductionOrder("PRD-1", from.Id, item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 500m);
        db.ProductionOrders.Add(order);

        db.SaveChanges();

        return new Fixture { From = from, To = to, Item = item, OtherItem = otherItem, Warehouse = warehouse, Message = message, Order = order };
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

    /// <summary>The two real accounts every authorised override must name.</summary>
    private static (string Operator, string Manager) SeedOperatorAndManager(ApplicationDbContext db)
    {
        SeedUsers(db,
            ("operator", "x", true),
            ("floor-manager", Permissions.InventoryApproveNegativeStock + "," + Permissions.InventoryAllowNegativeStock, true));
        return ("operator", "floor-manager");
    }

    private static CreateCustomerTransferCommandHandler TransferHandler(ApplicationDbContext db, ICurrentUserService user) =>
        new(db, user, new FixedNumberGenerator(), new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static CreateRawExternalReleaseCommandHandler ReleaseHandler(ApplicationDbContext db, ICurrentUserService user) =>
        new(db, user, new FixedNumberGenerator(), new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static CreateStockAdjustmentCommandHandler AdjustmentHandler(ApplicationDbContext db, ICurrentUserService user) =>
        new(db, user, new FixedNumberGenerator(), new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(),
            new RecordingStockLock());

    private static ResumeProductionOrderCommandHandler ResumeHandler(ApplicationDbContext db, ICurrentUserService user) =>
        new(db, user, new InventoryLedgerService(db), new FixedClock(), new OpenPeriod(), new RecordingStockLock());

    private static ICurrentUserService Overrider(string userName) => new FakeUser(userName,
        Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);

    /// <summary>Raises the separation-of-duties request exactly as the UI does (module command as JSON).</summary>
    private static Task<ApprovalRequestDto> SubmitAsync<T>(
        ApplicationDbContext db, ICurrentUserService requester, ApprovalRequestType type, string reason, T payload) =>
        new SubmitApprovalRequestCommandHandler(db, requester, new FixedClock())
            .Handle(new SubmitApprovalRequestCommand(type, reason, ApprovalPayload.Serialize(payload)), CancellationToken.None);

    private static async Task<decimal> BalanceAsync(ApplicationDbContext db, Fixture f, Guid customerId) =>
        (await new InventoryLedgerService(db).GetCustomerBalanceAsync(
            f.Message.Id, f.Item.Id, customerId, f.Warehouse.Id, CancellationToken.None)).Kg;

    // =====================================================================
    // 1. Customer transfer
    // =====================================================================

    private static CreateCustomerTransferCommand Transfer(
        Fixture f, Guid from, Guid to, decimal? kg, decimal? meter = null,
        bool over = false, string? reason = null, Guid? approvalId = null, Guid? item = null, Guid? message = null) =>
        new(from, to, message ?? f.Message.Id, item ?? f.Item.Id, kg, meter,
            "ownership correction", null, over, reason, approvalId);

    [Fact]
    public async Task CustomerTransfer_NegativeStock_IsStillBlockedByDefault()
    {
        using var db = NewDb();
        var f = Seed(db);

        var act = () => TransferHandler(db, new FakeUser("operator", Permissions.RawTransfer))
            .Handle(Transfer(f, f.From.Id, f.To.Id, 150m), CancellationToken.None);

        (await act.Should().ThrowAsync<NegativeStockException>())
            .WithMessage($"*{MessageBalanceKg}*");
        (await db.CustomerTransfers.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.CustomerTransfer)).Should().Be(0);
    }

    [Fact]
    public async Task CustomerTransfer_Override_WithoutAStoredApprovalRequest_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);

        var act = () => TransferHandler(db, Overrider("operator"))
            .Handle(Transfer(f, f.From.Id, f.To.Id, 150m, over: true, reason: "customer needs it today"),
                CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");

        (await db.CustomerTransfers.CountAsync()).Should().Be(0, "an unauthorised override posts nothing");
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.CustomerTransfer)).Should().Be(0);
    }

    [Fact]
    public async Task CustomerTransfer_TheRequester_CannotConsumeTheirOwnApproval()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, _) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "short by 50",
            Transfer(f, f.From.Id, f.To.Id, 150m));

        // The SAME account that asked now tries to execute with its own request id.
        var act = () => TransferHandler(db, Overrider(operatorName))
            .Handle(Transfer(f, f.From.Id, f.To.Id, 150m, over: true, reason: "short by 50", approvalId: submitted.Id),
                CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*cannot also approve it*");

        (await db.CustomerTransfers.CountAsync()).Should().Be(0);
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task CustomerTransfer_WithAValidApproval_Executes_AndRecordsTheLedgerAndBothUsers()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "customer order needs the full quantity",
            Transfer(f, f.From.Id, f.To.Id, 150m));

        await TransferHandler(db, Overrider(managerName))
            .Handle(Transfer(f, f.From.Id, f.To.Id, 150m, over: true,
                reason: "customer order needs the full quantity", approvalId: submitted.Id), CancellationToken.None);

        // The transfer posted, with the standard OUT/IN pair on the same lot.
        var transfer = await db.CustomerTransfers.SingleAsync();
        transfer.QuantityKg.Should().Be(150m);

        var movements = await db.InventoryTransactions
            .Where(t => t.SourceDocumentType == DocumentType.CustomerTransfer && t.SourceDocumentId == transfer.Id)
            .ToListAsync();
        movements.Should().HaveCount(2);
        movements.Single(m => m.Direction == TransactionDirection.Out).CustomerId.Should().Be(f.From.Id);
        movements.Single(m => m.Direction == TransactionDirection.In).CustomerId.Should().Be(f.To.Id);
        movements.Should().OnlyContain(m => m.QuantityKg == 150m);

        // Balances come from the ledger, never from an edited figure.
        (await BalanceAsync(db, f, f.From.Id)).Should().Be(-50m);
        (await BalanceAsync(db, f, f.To.Id)).Should().Be(150m);

        // The exception record names BOTH real accounts and the approved facts.
        var overrider = await db.NegativeStockOverrides.SingleAsync();
        overrider.RequestedBy.Should().Be(operatorName);
        overrider.ApprovedBy.Should().Be(managerName);
        overrider.RequestedQuantity.Should().Be(150m);
        overrider.BalanceBefore.Should().Be(MessageBalanceKg);
        overrider.ResultingBalance.Should().Be(-50m);
        overrider.Reason.Should().Be("customer order needs the full quantity");

        // The approval is consumed, with the decision recorded.
        var consumed = await db.ApprovalRequests.SingleAsync();
        consumed.Status.Should().Be(ApprovalRequestStatus.Approved);
        consumed.DecidedBy.Should().Be(managerName);
        consumed.DecidedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CustomerTransfer_Approval_IsSingleUse()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "short by 50", Transfer(f, f.From.Id, f.To.Id, 150m));

        var command = Transfer(f, f.From.Id, f.To.Id, 150m, over: true, reason: "short by 50", approvalId: submitted.Id);
        await TransferHandler(db, Overrider(managerName)).Handle(command, CancellationToken.None);

        var replay = () => TransferHandler(db, Overrider(managerName)).Handle(command, CancellationToken.None);

        (await replay.Should().ThrowAsync<DomainException>())
            .WithMessage("*already Approved*", "one approval authorises exactly one movement");

        (await db.CustomerTransfers.CountAsync()).Should().Be(1);
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(1);
    }

    [Theory]
    // quantity, unit, item, receiving account, sending account
    [InlineData("quantity")]
    [InlineData("unit")]
    [InlineData("item")]
    [InlineData("receiving-account")]
    [InlineData("sending-account")]
    public async Task CustomerTransfer_ChangingAnApprovedDetail_InvalidatesTheApproval(string changed)
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "approved for the original facts",
            Transfer(f, f.From.Id, f.To.Id, 150m));

        // The executor keeps the approval id but changes ONE approved fact.
        var executed = changed switch
        {
            "quantity" => Transfer(f, f.From.Id, f.To.Id, 400m, over: true, reason: "approved for the original facts", approvalId: submitted.Id),
            "unit" => Transfer(f, f.From.Id, f.To.Id, null, meter: 150m, over: true, reason: "approved for the original facts", approvalId: submitted.Id),
            "item" => Transfer(f, f.From.Id, f.To.Id, 150m, over: true, reason: "approved for the original facts", approvalId: submitted.Id, item: f.OtherItem.Id),
            "receiving-account" => Transfer(f, f.From.Id, Guid.NewGuid(), 150m, over: true, reason: "approved for the original facts", approvalId: submitted.Id),
            _ => Transfer(f, Guid.NewGuid(), f.To.Id, 150m, over: true, reason: "approved for the original facts", approvalId: submitted.Id)
        };

        var act = () => TransferHandler(db, Overrider(managerName)).Handle(executed, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*different operation details*");

        (await db.CustomerTransfers.CountAsync()).Should().Be(0, "a changed operation needs a new request");
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending,
            "the request is untouched, so it can still authorise the movement it WAS approved for");
    }

    [Fact]
    public async Task AnApproval_RaisedForAnotherOperation_CannotAuthoriseThisOne()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        // Approved as a customer transfer, then presented to the ADJUSTMENT
        // handler: a different operation entirely, so it cannot authorise it.
        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "short by 50", Transfer(f, f.From.Id, f.To.Id, 150m));

        var act = () => AdjustmentHandler(db, Overrider(managerName)).Handle(
            Adjustment(f, 150m, over: true, overrideReason: "short by 50", approvalId: submitted.Id),
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*was raised for*");

        (await db.StockAdjustments.CountAsync()).Should().Be(0);
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task CustomerTransfer_ApprovedThroughTheApprovalCenter_ExecutesTheModulesOwnCommand()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawTransfer),
            ApprovalRequestType.CustomerTransfer, "short by 50", Transfer(f, f.From.Id, f.To.Id, 150m));

        // The approver is a DIFFERENT, authenticated user; nothing about them is
        // in the request body - the handler takes their identity from the session.
        var approval = await BuildSender(db, Overrider(managerName))
            .Send(new ApproveApprovalRequestCommand(submitted.Id));

        approval.Status.Should().Be(nameof(ApprovalRequestStatus.Approved));
        approval.DecidedBy.Should().Be(managerName);

        (await db.CustomerTransfers.CountAsync()).Should().Be(1, "approving executes the module's own command");
        (await db.NegativeStockOverrides.SingleAsync()).ApprovedBy.Should().Be(managerName);
    }

    // =====================================================================
    // 2. Raw external release (return to customer / external processing / sale)
    // =====================================================================

    private static CreateRawExternalReleaseCommand Release(
        Fixture f, decimal? kg, RawReleaseReason reason = RawReleaseReason.ExternalProcessing,
        bool over = false, string? overrideReason = null, Guid? approvalId = null,
        Guid? customerId = null, Guid? itemId = null) =>
        new(customerId ?? f.From.Id, itemId ?? f.Item.Id, f.Message.Id, kg, null, reason,
            "External dyer", null, over, overrideReason, null, null, null, null, approvalId);

    [Fact]
    public async Task ExternalRelease_Override_WithoutAStoredApprovalRequest_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);

        var act = () => ReleaseHandler(db, Overrider("operator"))
            .Handle(Release(f, 150m, over: true, overrideReason: "processor is waiting"), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");

        (await db.RawExternalReleases.CountAsync()).Should().Be(0);
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.RawIssueExternalRelease)).Should().Be(0);
    }

    [Fact]
    public async Task ExternalRelease_WithAValidApproval_Executes_AndRecordsTheLedgerAndBothUsers()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawExternalRelease),
            ApprovalRequestType.RawExternalRelease, "processor holds more than our balance",
            Release(f, 150m));

        await ReleaseHandler(db, Overrider(managerName))
            .Handle(Release(f, 150m, over: true, overrideReason: "processor holds more than our balance",
                approvalId: submitted.Id), CancellationToken.None);

        var release = await db.RawExternalReleases.SingleAsync();
        release.QuantityKg.Should().Be(150m);
        release.Reason.Should().Be(RawReleaseReason.ExternalProcessing);

        var movement = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.RawIssueExternalRelease);
        movement.Direction.Should().Be(TransactionDirection.Out);
        movement.QuantityKg.Should().Be(150m);
        movement.CustomerId.Should().Be(f.From.Id);

        (await BalanceAsync(db, f, f.From.Id)).Should().Be(-50m);

        var overrider = await db.NegativeStockOverrides.SingleAsync();
        overrider.RequestedBy.Should().Be(operatorName);
        overrider.ApprovedBy.Should().Be(managerName);

        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task ExternalRelease_ChangingTheReleaseReason_InvalidatesTheApproval()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.RawExternalRelease),
            ApprovalRequestType.RawExternalRelease, "external processing",
            Release(f, 150m, RawReleaseReason.ExternalProcessing));

        // Approved as external processing, executed as a SALE: a different operation.
        var act = () => ReleaseHandler(db, Overrider(managerName))
            .Handle(Release(f, 150m, RawReleaseReason.Sale, over: true,
                overrideReason: "external processing", approvalId: submitted.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*different operation details*");

        (await db.RawExternalReleases.CountAsync()).Should().Be(0);
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    // =====================================================================
    // 3. Stock adjustment
    // =====================================================================

    private static CreateStockAdjustmentCommand Adjustment(
        Fixture f, decimal? kg, AdjustmentType type = AdjustmentType.Decrease, bool over = false,
        string? overrideReason = null, Guid? approvalId = null, Guid? itemId = null, Guid? customerId = null) =>
        new(customerId ?? f.From.Id, itemId ?? f.Item.Id, f.Message.Id, type, kg, null,
            "physical count variance", null, null, over, overrideReason, approvalId);

    [Fact]
    public async Task StockAdjustment_Override_WithoutAStoredApprovalRequest_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);

        var act = () => AdjustmentHandler(db, Overrider("operator"))
            .Handle(Adjustment(f, 150m, over: true, overrideReason: "count was short"), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");

        (await db.StockAdjustments.CountAsync()).Should().Be(0);
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(0);
        (await db.InventoryTransactions.CountAsync(t => t.SourceDocumentType == DocumentType.StockAdjustment)).Should().Be(0);
    }

    [Fact]
    public async Task StockAdjustment_WithAValidApproval_Executes_AndStoresTheRealApprover()
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.InventoryAdjust),
            ApprovalRequestType.StockAdjustment, "physical count variance of 50 KG",
            Adjustment(f, 150m));

        await AdjustmentHandler(db, Overrider(managerName))
            .Handle(Adjustment(f, 150m, over: true, overrideReason: "physical count variance of 50 KG",
                approvalId: submitted.Id), CancellationToken.None);

        var adjustment = await db.StockAdjustments.SingleAsync();
        adjustment.Type.Should().Be(AdjustmentType.Decrease);
        adjustment.QuantityBeforeKg.Should().Be(MessageBalanceKg);
        adjustment.AdjustmentQuantityKg.Should().Be(150m);
        adjustment.QuantityAfterKg.Should().Be(-50m);
        adjustment.ApprovedBy.Should().Be(managerName,
            "the authorising account comes from the approval, never from a typed name in the body");

        var movement = await db.InventoryTransactions
            .SingleAsync(t => t.SourceDocumentType == DocumentType.StockAdjustment);
        movement.Direction.Should().Be(TransactionDirection.Out);
        movement.QuantityKg.Should().Be(150m);

        (await BalanceAsync(db, f, f.From.Id)).Should().Be(-50m);

        var overrider = await db.NegativeStockOverrides.SingleAsync();
        overrider.RequestedBy.Should().Be(operatorName);
        overrider.ApprovedBy.Should().Be(managerName);
        overrider.BalanceBefore.Should().Be(MessageBalanceKg);
        overrider.ResultingBalance.Should().Be(-50m);
        overrider.Reason.Should().Be("physical count variance of 50 KG");

        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Approved);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("item")]
    [InlineData("account")]
    public async Task StockAdjustment_ChangingAnApprovedDetail_InvalidatesTheApproval(string changed)
    {
        using var db = NewDb();
        var f = Seed(db);
        var (operatorName, managerName) = SeedOperatorAndManager(db);

        var submitted = await SubmitAsync(db, new FakeUser(operatorName, Permissions.InventoryAdjust),
            ApprovalRequestType.StockAdjustment, "approved for the original facts", Adjustment(f, 150m));

        var executed = changed switch
        {
            "quantity" => Adjustment(f, 400m, over: true, overrideReason: "approved for the original facts", approvalId: submitted.Id),
            "item" => Adjustment(f, 150m, over: true, overrideReason: "approved for the original facts", approvalId: submitted.Id, itemId: f.OtherItem.Id),
            _ => Adjustment(f, 150m, over: true, overrideReason: "approved for the original facts", approvalId: submitted.Id, customerId: Guid.NewGuid())
        };

        var act = () => AdjustmentHandler(db, Overrider(managerName)).Handle(executed, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*different operation details*");

        (await db.StockAdjustments.CountAsync()).Should().Be(0);
        (await db.ApprovalRequests.SingleAsync()).Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    // =====================================================================
    // 4. Resume (the same bypass existed on the pause/resume re-issue)
    // =====================================================================

    [Fact]
    public async Task Resume_Override_WithoutAStoredApprovalRequest_IsRefused()
    {
        using var db = NewDb();
        var f = Seed(db);

        var act = () => ResumeHandler(db, Overrider("operator")).Handle(
            new ResumeProductionOrderCommand(f.Order.Id, f.Message.Id, f.Item.Id, 150m, null,
                OverrideNegativeStock: true, OverrideReason: "restarting the job", ApprovalRequestId: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");

        (await db.RawAllocations.CountAsync()).Should().Be(0);
        (await db.NegativeStockOverrides.CountAsync()).Should().Be(0);
    }

    // =====================================================================

    /// <summary>A real MediatR pipeline, so approving dispatches the module's own command.</summary>
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
        services.AddSingleton<IInventoryLedgerService>(new InventoryLedgerService(db));
        services.AddSingleton<IDocumentNumberGenerator>(new FixedNumberGenerator());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }
}
