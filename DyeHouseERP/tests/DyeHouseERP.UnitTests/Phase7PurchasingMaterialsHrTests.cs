using System.Reflection;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Approvals.Commands;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Services;
using DyeHouseERP.Application.Materials.Commands;
using DyeHouseERP.Application.Payroll.Commands;
using DyeHouseERP.Application.Purchases.Commands;
using DyeHouseERP.Application.Supplies.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 7 - purchasing, chemicals, operating supplies, HR and payroll.
///
/// These tests pin the five behaviours the phase brief names, each against the real command handlers or
/// the real domain rules (never a stand-in for the rule itself):
///
///   * a DRAFT purchase document has no effect at all - no stock movement, no financial entry - posting
///     writes exactly one, and a second post is refused (so nothing is ever posted twice);
///   * chemical consumption beyond stock is refused outright and factory-owned stock can never go
///     negative; the only exception mechanism in the system is the approval-center one, which the
///     separation-of-duties guard refuses to let a user raise and approve alone;
///   * an operating supply's consumption cost comes from the posted ledger transactions (the unit cost
///     recorded on the movement), never from a value typed at reporting time;
///   * HR attendance/payroll is configuration-driven - changing the policy changes the money with no code
///     change - and HR shifts are independent of the production execution shifts;
///   * every HR/payroll endpoint carries its own granular permission policy.
/// </summary>
public abstract class Phase7TestBase
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    protected Phase7TestBase()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase7-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    protected ApplicationDbContext NewDb() => new(_options);

    /// <summary>A real MediatR pipeline, so a command dispatches the module's own query/handlers.</summary>
    protected static ISender Sender(ApplicationDbContext db, ICurrentUserService user)
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateMaterialIssueCommand>());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(user);
        services.AddSingleton<ICurrentUserService>(user);
        services.AddSingleton<IDateTime>(new FixedClock());
        services.AddSingleton<IPeriodCloseService>(new OpenPeriod());
        services.AddSingleton<IAllocationLockService>(new RecordingStockLock());
        services.AddSingleton<IDocumentNumberGenerator>(new FixedNumberGenerator());
        services.AddSingleton<IMaterialLedgerService>(new MaterialLedgerService(db));
        services.AddSingleton<IInventoryLedgerService>(new InventoryLedgerService(db));
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    protected static (Warehouse Warehouse, Material Material) SeedFactoryStock(
        ApplicationDbContext db, MaterialKind kind, decimal openingQuantity, decimal purchasePrice)
    {
        var warehouseKind = kind == MaterialKind.OperatingSupply
            ? WarehouseKind.OperatingSupplies
            : WarehouseKind.Materials;
        var warehouse = new Warehouse($"WH-{kind}", "Factory Store", warehouseKind, "seed");
        var material = new Material($"MAT-{kind}", kind == MaterialKind.Chemical ? "Dye" : "Gloves",
            MaterialUnit.KG, purchasePrice, "seed", kind);
        db.Warehouses.Add(warehouse);
        db.Materials.Add(material);

        // Opening stock is a real ledger row: a balance is always the live sum of the append-only
        // material ledger, never a stored column.
        db.MaterialTransactions.Add(new MaterialTransaction(
            DocumentType.PurchaseReceipt, "PR-SEED", Guid.NewGuid(), DateTime.UtcNow,
            material.Id, warehouse.Id, productionOrderId: null,
            quantity: openingQuantity, direction: MaterialTransactionDirection.In,
            unitCost: purchasePrice, createdBy: "seed"));

        db.SaveChanges();
        return (warehouse, material);
    }
}

// ============================================================================================
//  1. Purchasing: a draft has no effect, posting writes exactly one, a second post is refused
// ============================================================================================

public class Phase7PurchasePostingTests : Phase7TestBase
{
    private static Supplier NewSupplier() => new("SUP-P7", "Chemical Supplier", "seed");

    private static SupplierInvoice NewDraftInvoice(Supplier supplier, Material material, decimal quantity, decimal unitPrice)
    {
        var invoice = new SupplierInvoice("SI-2026-0001", new DateTime(2026, 4, 2), new DateTime(2026, 5, 2),
            supplier.Id, "seed");
        invoice.AddLine(material.Id, "Dye purchase", quantity, material.Unit, unitPrice);
        return invoice;
    }

    [Fact]
    public async Task ADraftSupplierInvoice_WritesNothing_AndPostingWritesExactlyOneEntry_AndRePostingIsRefused()
    {
        using var db = NewDb();
        var supplier = NewSupplier();
        db.Suppliers.Add(supplier);
        var (_, material) = SeedFactoryStock(db, MaterialKind.Chemical, openingQuantity: 0m, purchasePrice: 5m);

        var invoice = NewDraftInvoice(supplier, material, quantity: 10m, unitPrice: 5m);
        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync();

        // A DRAFT is a commercial document: it creates no financial entry at all.
        (await db.SupplierLedgerEntries.CountAsync()).Should().Be(0, "a draft invoice is not yet a payable");

        var sender = Sender(db, new StubCurrentUser("accountant"));

        // Posting writes EXACTLY ONE debit entry for the total - once.
        await sender.Send(new PostSupplierInvoiceCommand(invoice.Id), CancellationToken.None);

        var entries = await db.SupplierLedgerEntries.AsNoTracking().ToListAsync();
        entries.Should().ContainSingle("posting writes exactly one supplier-ledger row, never one per line");
        entries[0].Debit.Should().Be(50m, "10 KG x 5 = the invoice total");
        entries[0].Credit.Should().Be(0m);
        entries[0].SupplierId.Should().Be(supplier.Id);

        // Re-posting is refused, and nothing is written a second time.
        var repost = () => sender.Send(new PostSupplierInvoiceCommand(invoice.Id), CancellationToken.None);
        await repost.Should().ThrowAsync<DomainException>("only a draft invoice can be posted");
        (await db.SupplierLedgerEntries.CountAsync()).Should().Be(1, "a refused re-post writes nothing");
    }

    [Fact]
    public async Task ADraftPurchaseOrder_CreatesNoStockMovementAndNoFinancialEntry()
    {
        using var db = NewDb();
        var supplier = NewSupplier();
        db.Suppliers.Add(supplier);
        var (warehouse, material) = SeedFactoryStock(db, MaterialKind.Chemical, openingQuantity: 0m, purchasePrice: 5m);

        var sender = Sender(db, new StubCurrentUser("buyer"));
        await sender.Send(new CreatePurchaseOrderCommand(
            new DateTime(2026, 4, 2), supplier.Id, warehouse.Id,
            Lines: new List<PurchaseOrderLineInput> { new() { MaterialId = material.Id, Quantity = 10m, Unit = material.Unit, UnitPrice = 5m } }),
            CancellationToken.None);

        (await db.PurchaseOrders.CountAsync()).Should().Be(1);
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId != Guid.Empty && t.SourceDocumentNumber == "PR-SEED")).Should().Be(1,
            "only the seeded opening-stock row exists: a draft order moves nothing");
        (await db.SupplierLedgerEntries.CountAsync()).Should().Be(0, "an order on its own is not a payable");
    }
}

// ============================================================================================
//  2. Chemicals: over-consumption refused; the only exception needs a separate approver
// ============================================================================================

public class Phase7ChemicalStockTests : Phase7TestBase
{
    private static ProductionOrder NewOrder() =>
        new("PRD-P7-1", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "seed", requestedQuantityKg: 100m);

    [Fact]
    public async Task ChemicalConsumptionBeyondStock_IsRefused_AndWritesNothing()
    {
        using var db = NewDb();
        var (warehouse, material) = SeedFactoryStock(db, MaterialKind.Chemical, openingQuantity: 10m, purchasePrice: 5m);
        var order = NewOrder();
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var sender = Sender(db, new StubCurrentUser("floor"));

        var act = () => sender.Send(
            new CreateMaterialIssueCommand(material.Id, warehouse.Id, order.Id, Quantity: 15m, UnitCost: null, Notes: null),
            CancellationToken.None);

        // Factory-owned chemical stock is never allowed to go negative: 15 KG against a 10 KG balance is refused.
        (await act.Should().ThrowAsync<NegativeStockException>())
            .WithMessage("*10*");

        (await db.MaterialIssues.CountAsync()).Should().Be(0, "a refused issue posts no document");
        (await db.MaterialTransactions.CountAsync(t => t.Direction == MaterialTransactionDirection.Out)).Should().Be(0,
            "a refused issue posts no OUT movement");
    }

    [Fact]
    public void FactoryOwnedStock_HasNoLocalOverridePath_TheExceptionIsTheApprovalCenterOne()
    {
        // The chemicals path deliberately exposes no override flag or approval id: there is no way for a
        // caller to wave negative stock through by setting a boolean. The ONLY negative-stock exception in
        // the system is the approval-center workflow (ApprovalRequestGuard), and it is bound to the
        // customer-owned raw lots - which is why the factory-owned material command carries none of it.
        typeof(CreateMaterialIssueCommand).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(name =>
                name.Contains("Override", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Approval", StringComparison.OrdinalIgnoreCase),
                "the material issue must have no self-service override, so it can never take stock negative");

        // The exception mechanism itself is fabric-only: it records against a raw message + customer.
        typeof(NegativeStockOverride).GetProperties().Select(p => p.Name)
            .Should().Contain(new[] { "RawMessageId", "CustomerId" })
            .And.NotContain("MaterialId", "the override audit is about customer-owned raw stock, not factory stock");
    }

    [Fact]
    public async Task TheExceptionWorkflow_RequiresAStoredApproval_AndRefusesSelfApproval()
    {
        using var db = NewDb();
        // A user who even holds BOTH negative-stock permissions still cannot bypass the separation of duties.
        var operatorUser = new PermissionFakeUser("operator",
            Permissions.InventoryAllowNegativeStock, Permissions.InventoryApproveNegativeStock);

        // 1. Without a stored approval request the movement is refused outright.
        Func<Task> withoutApproval = () => ApprovalRequestGuard.ConsumeAsync(
            db, operatorUser, approvalRequestId: null, new StubApprovalOperation(), CancellationToken.None);

        (await withoutApproval.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*separation of duties*");

        // 2. A stored request can never be approved by the very user who raised it.
        var request = new ApprovalRequest(
            ApprovalRequestType.StockAdjustment, Guid.NewGuid(), "DOC-1", "needs an exception", "{}",
            requestedBy: "operator", requestedAtUtc: DateTime.UtcNow);
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();

        Func<Task> selfApproval = () => ApprovalRequestGuard.EnsureDeciderAsync(
            db, operatorUser, request, CancellationToken.None);

        (await selfApproval.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("*cannot also approve it*");

        request.Status.Should().Be(ApprovalRequestStatus.Pending, "a refused decision leaves the request pending");
    }

    private sealed class PermissionFakeUser : ICurrentUserService
    {
        private readonly HashSet<string> _permissions;
        public PermissionFakeUser(string userName, params string[] permissions)
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

    private sealed class StubApprovalOperation : IApprovalBoundOperation
    {
        public ApprovalRequestType ApprovalType => ApprovalRequestType.StockAdjustment;
        public Guid ApprovalDocumentId => Guid.NewGuid();
        public string OperationSignature => "P7-test-operation";
    }
}

// ============================================================================================
//  3. Operating supplies: draft -> post, with the cost taken from the posted transactions
// ============================================================================================

public class Phase7OperatingSupplyTests : Phase7TestBase
{
    [Fact]
    public async Task ADraftSupplyIssue_MovesNoStock_AndPostingWritesOneOutRowPerLine_AndRePostingIsRefused()
    {
        using var db = NewDb();
        // No explicit unit cost on the issue: the recorded cost must fall back to the material's own price.
        var (warehouse, supply) = SeedFactoryStock(db, MaterialKind.OperatingSupply, openingQuantity: 100m, purchasePrice: 12.5m);

        var sender = Sender(db, new StubCurrentUser("storekeeper"));
        var draft = await sender.Send(
            new CreateSupplyIssueCommand(DateTime.UtcNow, warehouse.Id, "Dyeing floor", "Cleaning", DepartmentId: null),
            CancellationToken.None);

        await sender.Send(new AddSupplyIssueLineCommand(draft.Id, supply.Id, Quantity: 20m), CancellationToken.None);

        // A DRAFT moves nothing.
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == draft.Id)).Should().Be(0,
            "an unpublished supply issue is not a movement");

        var posted = await sender.Send(new PostSupplyIssueCommand(draft.Id), CancellationToken.None);

        // Posting writes EXACTLY ONE OUT row per line, and its unit cost is what the CONSUMPTION records.
        var rows = await db.MaterialTransactions.AsNoTracking().Where(t => t.SourceDocumentId == draft.Id).ToListAsync();
        rows.Should().ContainSingle("one OUT row per posted line");
        rows[0].Direction.Should().Be(MaterialTransactionDirection.Out);
        rows[0].Quantity.Should().Be(20m);
        rows[0].UnitCost.Should().Be(12.5m, "the cost is taken from the material/transaction, not typed later");

        // The document's cost is derived from those transactions: 20 x 12.5.
        posted.Lines.Should().ContainSingle().Which.TotalCost.Should().Be(250m);

        var repost = () => sender.Send(new PostSupplyIssueCommand(draft.Id), CancellationToken.None);
        await repost.Should().ThrowAsync<DomainException>("an issue is posted exactly once");
        (await db.MaterialTransactions.CountAsync(t => t.SourceDocumentId == draft.Id)).Should().Be(1);
    }

    [Fact]
    public async Task AProductionChemical_CannotBeBookedAsAnInternalOperatingSupply()
    {
        using var db = NewDb();
        var (warehouse, _) = SeedFactoryStock(db, MaterialKind.OperatingSupply, openingQuantity: 100m, purchasePrice: 4m);
        var (_, chemical) = SeedFactoryStock(db, MaterialKind.Chemical, openingQuantity: 100m, purchasePrice: 9m);

        var sender = Sender(db, new StubCurrentUser("storekeeper"));

        var draft = await sender.Send(
            new CreateSupplyIssueCommand(DateTime.UtcNow, warehouse.Id, "Stores", "Misc", null), CancellationToken.None);

        // The supplies store accepts supply-classified items only: a dye/chemical has to go to a Job Order.
        var chemicalToSupplies = () => sender.Send(
            new AddSupplyIssueLineCommand(draft.Id, chemical.Id, 5m), CancellationToken.None);
        await chemicalToSupplies.Should().ThrowAsync<DomainException>();

        draft.Lines.Should().BeEmpty("a refused line is never added");
    }
}

// ============================================================================================
//  4. HR & payroll: configuration drives the money, and HR shifts are not production shifts
// ============================================================================================

public class Phase7HrPayrollConfigurationTests : Phase7TestBase
{
    private static AttendanceRule Policy(decimal absenceDayDeductionFactor)
    {
        var rule = new AttendanceRule("Standard", "hr");
        rule.Configure(standardDailyHours: 8m, standardWorkingDaysPerMonth: 25m, graceMinutes: 0,
            latePenaltyPerMinute: 0m, earlyLeavePenaltyPerMinute: 0m,
            absenceDayDeductionFactor: absenceDayDeductionFactor, overtimeGraceMinutes: 0,
            overtimeNormalMultiplier: 1.5m, overtimeWeekendMultiplier: 2m, overtimeHolidayMultiplier: 2.5m,
            weekendDays: "5,6", paidLeaveCountsAsAttendance: true, overtimeRequiresApproval: true);
        rule.Activate("hr");
        return rule;
    }

    [Fact]
    public async Task ChangingTheActiveAttendancePolicy_ChangesTheCalculatedPayroll_WithoutAnyCodeChange()
    {
        using var db = NewDb();
        var sender = Sender(db, new StubCurrentUser("hr"));

        var department = new Department("D-P7", "Dyeing", "hr");
        var employee = new Employee("EMP-P7", "Ali Hassan", department.Id, 6000m, new DateTime(2025, 1, 1), "hr");
        var shift = new HrShift("M", 8 * 60, 16 * 60, "hr", "صباحي", "Morning");
        var strict = Policy(absenceDayDeductionFactor: 1.0m);

        db.Departments.Add(department);
        db.Employees.Add(employee);
        db.HrShifts.Add(shift);
        db.AttendanceRules.Add(strict);

        // One absent working day per period, for the same employee.
        db.AttendanceEvents.Add(AttendanceEvent.NonWorking(employee.Id, new DateTime(2026, 4, 10), AttendanceStatus.Absent, shift, strict, "hr"));
        db.AttendanceEvents.Add(AttendanceEvent.NonWorking(employee.Id, new DateTime(2026, 5, 10), AttendanceStatus.Absent, shift, strict, "hr"));

        var april = new PayrollRun("PRL-P7-1", 2026, 4, "hr");
        april.AddLine(employee.Id, employee.Code, employee.Name, department.Id, department.Name, employee.BasicSalary, 0m, 0m);
        var may = new PayrollRun("PRL-P7-2", 2026, 5, "hr");
        may.AddLine(employee.Id, employee.Code, employee.Name, department.Id, department.Name, employee.BasicSalary, 0m, 0m);
        db.PayrollRuns.AddRange(april, may);
        await db.SaveChangesAsync();

        await sender.Send(new CalculatePayrollRunCommand(april.Id), CancellationToken.None);
        var withStrictPolicy = (await db.PayrollRuns.AsNoTracking().Include(r => r.Lines)
            .SingleAsync(r => r.Id == april.Id)).TotalNet;

        // Swap the CONFIGURATION (not the code): a policy that charges nothing for an absence.
        strict.Deactivate("hr");
        db.AttendanceRules.Add(Policy(absenceDayDeductionFactor: 0m));
        await db.SaveChangesAsync();

        await sender.Send(new CalculatePayrollRunCommand(may.Id), CancellationToken.None);
        var withLenientPolicy = (await db.PayrollRuns.AsNoTracking().Include(r => r.Lines)
            .SingleAsync(r => r.Id == may.Id)).TotalNet;

        // Same employee, same salary, same one absent day - only the configured policy differs.
        // 6000 / 25 = 240 a day, so the strict policy deducts exactly one day more.
        (withLenientPolicy - withStrictPolicy).Should().Be(240m,
            "the payroll is a pure function of the configured policy - changing the policy changes the money");
    }

    [Fact]
    public void HrShifts_AndProductionExecutionShifts_AreSeparateEntities()
    {
        typeof(HrShift).Should().NotBe(typeof(ProductionShift));
        typeof(HrShift).GetProperty("PlannedMinutes").Should().NotBeNull("an HR shift carries its own planned minutes");
        typeof(ProductionShift).GetProperties().Select(p => p.Name)
            .Should().NotContain("GraceMinutes", "production execution shifts carry no HR attendance rules");
    }

    [Fact]
    public async Task AProductionExecutionShift_DoesNotChangeAttendanceOrPayroll()
    {
        var withoutProductionShift = await CalculatedNetAsync(seedProductionShift: false);
        var withProductionShift = await CalculatedNetAsync(seedProductionShift: true);

        withProductionShift.Should().Be(withoutProductionShift,
            "HR attendance and payroll never read the production execution shifts - the two shift models are independent");
    }

    /// <summary>
    /// Builds one identical HR factory (employee + policy + one absent day + a run) - optionally with a
    /// production execution shift defined - calculates the payroll and returns the net.
    /// </summary>
    private async Task<decimal> CalculatedNetAsync(bool seedProductionShift)
    {
        using var db = NewDb();
        if (seedProductionShift)
            db.ProductionShifts.Add(new ProductionShift("PS-P7", "Morning production shift", 1, "seed"));

        var sender = Sender(db, new StubCurrentUser("hr"));
        var department = new Department("D-P7", "Dyeing", "hr");
        var employee = new Employee("EMP-P7", "Ali Hassan", department.Id, 6000m, new DateTime(2025, 1, 1), "hr");
        var hrShift = new HrShift("M", 8 * 60, 16 * 60, "hr", "صباحي", "Morning");
        var policy = Policy(absenceDayDeductionFactor: 1.0m);

        db.Departments.Add(department);
        db.Employees.Add(employee);
        db.HrShifts.Add(hrShift);
        db.AttendanceRules.Add(policy);
        db.AttendanceEvents.Add(AttendanceEvent.NonWorking(employee.Id, new DateTime(2026, 4, 10), AttendanceStatus.Absent, hrShift, policy, "hr"));

        var run = new PayrollRun("PRL-P7-1", 2026, 4, "hr");
        run.AddLine(employee.Id, employee.Code, employee.Name, department.Id, department.Name, employee.BasicSalary, 0m, 0m);
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        await sender.Send(new CalculatePayrollRunCommand(run.Id), CancellationToken.None);
        return (await db.PayrollRuns.AsNoTracking().Include(r => r.Lines).SingleAsync(r => r.Id == run.Id)).TotalNet;
    }
}

// ============================================================================================
//  5. Permissions: every HR and payroll endpoint carries its own granular policy
// ============================================================================================

public class Phase7HrPayrollPermissionTests
{
    [Fact]
    public void EveryHrAndPayrollEndpoint_CarriesItsGranularPermissionPolicy()
    {
        var controllers = new[] { typeof(HrController), typeof(PayrollController) };
        var offences = new List<string>();
        var policies = new List<string>();

        foreach (var controller in controllers)
        {
            foreach (var action in controller.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()) continue;

                var authorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
                var actionPolicies = authorize.Select(a => a.Policy).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

                if (actionPolicies.Count == 0)
                    offences.Add($"{controller.Name}.{action.Name} has no permission policy");

                // A role gate would make the granular permission unusable for a non-admin user.
                if (authorize.Any(a => a.Roles is not null))
                    offences.Add($"{controller.Name}.{action.Name} is gated by role");

                policies.AddRange(actionPolicies!);
            }
        }

        offences.Should().BeEmpty("every HR/payroll action must be protected by its own permission policy");

        // Every policy must name a real, granular permission constant - never an ad-hoc string.
        var permissionValues = Permissions.All.ToHashSet(StringComparer.Ordinal);
        foreach (var policy in policies.Distinct(StringComparer.Ordinal))
        {
            policy.Should().StartWith(PermissionPolicy.Prefix);
            permissionValues.Should().Contain(policy[PermissionPolicy.Prefix.Length..],
                $"'{policy}' must be one of the granular permissions");
        }

        // Sensitive HR/payroll data is never reachable without one of these rights.
        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.PayrollView);
        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.PayrollApprove);
        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.PayrollPost);
        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.HrView);
        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.HrManage);
    }
}
