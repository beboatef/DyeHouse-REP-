using DyeHouseERP.Application.Accounting.DTOs;
using DyeHouseERP.Application.Accounting.Queries;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Kpis.Queries;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// One test per KPI (spec Part 1), each with a hand-computed expected value.
/// All scenarios run against the InMemory provider; the SQL-translation proofs
/// live in KpiSqlTranslationTests so these tests only pin the numbers.
/// </summary>
public class KpiQueryTests
{
    private DbContextOptions<ApplicationDbContext> NewOptions(string name)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"kpi-{name}-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        var customer = new Customer("C-1", "Customer One", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var ready = new Warehouse("RDY", "Ready Goods", WarehouseKind.ReadyGoods, "seed");
        var formation = new ProductionStageDefinition("PS001", "Formation", 1, "seed", isFormationStage: true);
        db.AddRange(customer, item, ready, formation);
        await db.SaveChangesAsync();
    }

    // ---------- 1. Waste rate ----------

    /// <summary>
    /// Order raw 1000kg, stage input 1000 / output 900 / separates 20 / second
    /// grade 50 -&gt; waste = 1000-900-20-50 = 30kg = 3.00%. A second order with
    /// zero raw contributes 0kg waste and a null rate (division by zero).
    /// </summary>
    [Fact]
    public async Task WasteRate_ReturnsHandComputedRate_AndNullOnZeroRaw()
    {
        await using var db = new ApplicationDbContext(NewOptions("waste"));
        await SeedAsync(db);
        var scene = (await db.Customers.SingleAsync(), await db.Items.SingleAsync(), await db.ProductionStageDefinitions.SingleAsync());

        var order = new ProductionOrder("PRD-001", scene.Item1.Id, scene.Item2.Id, new DateTime(2026, 3, 1), "seed", requestedQuantityKg: 1000m);
        order.BuildStageRoute(new[] { scene.Item3 }, "seed");
        order.StageExecutions.Single().CloseWithOutput(
            outputKg: 900m, outputMeter: null, separatesKg: 20m, separatesMeter: null,
            notes: null, completedBy: "seed", inputKg: 1000m, secondGradeKg: 50m);
        db.ProductionOrders.Add(order);

        var zeroRaw = new ProductionOrder("PRD-002", scene.Item1.Id, scene.Item2.Id, new DateTime(2026, 3, 2), "seed", requestedQuantityKg: 0m);
        db.ProductionOrders.Add(zeroRaw);
        await db.SaveChangesAsync();

        var result = await new GetWasteRateKpiQueryHandler(db)
            .Handle(new GetWasteRateKpiQuery(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)), CancellationToken.None);

        result.Orders.Should().HaveCount(2);
        var first = result.Orders.Single(o => o.OrderNumber == "PRD-001");
        first.WasteKg.Should().Be(30m);
        first.WasteRatePct.Should().Be(3.00m);
        var second = result.Orders.Single(o => o.OrderNumber == "PRD-002");
        second.WasteKg.Should().Be(0m);
        second.WasteRatePct.Should().BeNull();
        result.TotalRawKg.Should().Be(1000m);
        result.TotalWasteKg.Should().Be(30m);
        result.OverallRatePct.Should().Be(3.00m);
        result.OrdersWithWaste.Should().Be(1);
        result.TimeZone.Should().Be("UTC");
    }

    // ---------- 2. Lead time ----------

    /// <summary>
    /// Order A issued Jan 1, first ready transfer Jan 6 -&gt; 5 days. Order B
    /// issued Jan 2, first transfer Jan 12 -&gt; 10 days. Order C never
    /// transferred -&gt; excluded. Average = 7.5, min 5, max 10, count 2.
    /// </summary>
    [Fact]
    public async Task LeadTime_AveragesOnlyFinishedOrdersInRange()
    {
        await using var db = new ApplicationDbContext(NewOptions("lead"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.SingleAsync();
        var ready = await db.Warehouses.SingleAsync(w => w.Kind == WarehouseKind.ReadyGoods);
        var formation = await db.ProductionStageDefinitions.SingleAsync();

        ProductionOrder Issue(string number, DateTime date)
        {
            var order = new ProductionOrder(number, customer.Id, item.Id, date, "seed", requestedQuantityKg: 100m);
            order.BuildStageRoute(new[] { formation }, "seed");
            db.ProductionOrders.Add(order);
            db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ProductionOrder, number, order.Id, date,
                ready.Id, customer.Id, item.Id, rawMessageId: Guid.NewGuid(), productionOrderId: order.Id,
                quantityKg: 100m, quantityMeter: null, direction: TransactionDirection.Out, createdBy: "seed"));
            return order;
        }

        void Transfer(ProductionOrder order, DateTime date)
        {
            var transfer = new ReadyGoodsTransfer(
                $"RGT-{order.OrderNumber}", date, order.Id, customer.Id, item.Id,
                ready.Id, 100m, null, "seed", category: ReadyGoodsCategory.FirstGrade);
            db.ReadyGoodsTransfers.Add(transfer);
            db.InventoryTransactions.Add(new InventoryTransaction(
                DocumentType.ReadyGoodsTransfer, transfer.TransferNumber, transfer.Id, date,
                ready.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: order.Id,
                quantityKg: 100m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed",
                readyGoodsCategory: ReadyGoodsCategory.FirstGrade));
        }

        var a = Issue("PRD-A", new DateTime(2026, 1, 1));
        Transfer(a, new DateTime(2026, 1, 6));
        Transfer(a, new DateTime(2026, 1, 20)); // later transfer is ignored: first one wins
        var b = Issue("PRD-B", new DateTime(2026, 1, 2));
        Transfer(b, new DateTime(2026, 1, 12));
        Issue("PRD-C", new DateTime(2026, 1, 3)); // unfinished: no transfer
        await db.SaveChangesAsync();

        var result = await new GetLeadTimeKpiQueryHandler(db)
            .Handle(new GetLeadTimeKpiQuery(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)), CancellationToken.None);

        result.FinishedOrders.Should().Be(2);
        result.Orders.Should().HaveCount(2);
        result.Orders.Single(o => o.OrderNumber == "PRD-A").LeadTimeDays.Should().Be(5);
        result.Orders.Single(o => o.OrderNumber == "PRD-B").LeadTimeDays.Should().Be(10);
        result.AverageLeadTimeDays.Should().Be(7.5);
        result.MinLeadTimeDays.Should().Be(5);
        result.MaxLeadTimeDays.Should().Be(10);
    }

    // ---------- 3. Machine utilisation ----------

    /// <summary>
    /// One 8h shift, 3-day period -&gt; available = 24h. The completed execution
    /// runs "now" so it is fully inside the window: running equals the
    /// execution's own elapsed time, utilisation = running / 24.
    /// </summary>
    [Fact]
    public async Task MachineUtilisation_ComputesPlantDenominator()
    {
        await using var db = new ApplicationDbContext(NewOptions("util"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.SingleAsync();
        var formation = await db.ProductionStageDefinitions.SingleAsync();
        var machine = new Machine("M-1", "Dyeing Machine", "seed");
        var shift = new ProductionShift("SHIFT-1", "First", 1, "seed", hoursPerDay: 8m);
        db.AddRange(machine, shift);

        var order = new ProductionOrder("PRD-U", customer.Id, item.Id, DateTime.UtcNow.Date.AddDays(-2), "seed", requestedQuantityKg: 100m);
        order.BuildStageRoute(new[] { formation }, "seed");
        // BuildStageRoute already activates the first execution; closing it directly keeps real timestamps.
        var exec = order.StageExecutions.Single();
        exec.CloseWithOutput(
            outputKg: 95m, outputMeter: null, separatesKg: null, separatesMeter: null,
            notes: null, completedBy: "seed", inputKg: 100m, machineId: machine.Id, productionShiftId: shift.Id);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var from = DateTime.UtcNow.Date.AddDays(-2);
        var result = await new GetMachineUtilisationKpiQueryHandler(db)
            .Handle(new GetMachineUtilisationKpiQuery(from, from.AddDays(2)), CancellationToken.None);

        result.DaysInPeriod.Should().Be(3);
        var expectedRunning = Math.Round((exec.CompletedAtUtc!.Value - exec.StartedAtUtc!.Value).TotalHours, 2);
        var machineRow = result.Machines.Should().ContainSingle().Subject;
        machineRow.MachineCode.Should().Be("M-1");
        machineRow.RunningHours.Should().Be(expectedRunning);
        machineRow.AvailableHours.Should().Be(24);
        machineRow.UtilisationPct.Should().Be((decimal)Math.Round(expectedRunning / 24 * 100, 2));
        var shiftRow = result.Shifts.Should().ContainSingle().Subject;
        shiftRow.HoursPerDay.Should().Be(8m);
        shiftRow.AvailableHours.Should().Be(24);
        result.TotalAvailableHours.Should().Be(24);
        result.OverallUtilisationPct.Should().Be(machineRow.UtilisationPct);
    }

    /// <summary>
    /// With no active shifts the available time is 0, so every utilisation
    /// ratio is null - never 0 or Infinity.
    /// </summary>
    [Fact]
    public async Task MachineUtilisation_ReturnsNull_WhenNoAvailableTime()
    {
        await using var db = new ApplicationDbContext(NewOptions("util-null"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.SingleAsync();
        var formation = await db.ProductionStageDefinitions.SingleAsync();
        var machine = new Machine("M-1", "Dyeing Machine", "seed");
        var shift = new ProductionShift("SHIFT-1", "First", 1, "seed", hoursPerDay: 8m);
        shift.Deactivate("seed");
        db.AddRange(machine, shift);

        var order = new ProductionOrder("PRD-U", customer.Id, item.Id, DateTime.UtcNow.Date, "seed", requestedQuantityKg: 100m);
        order.BuildStageRoute(new[] { formation }, "seed");
        var exec = order.StageExecutions.Single();
        exec.CloseWithOutput(
            outputKg: 95m, outputMeter: null, separatesKg: null, separatesMeter: null,
            notes: null, completedBy: "seed", inputKg: 100m, machineId: machine.Id, productionShiftId: shift.Id);
        db.ProductionOrders.Add(order);
        await db.SaveChangesAsync();

        var from = DateTime.UtcNow.Date;
        var result = await new GetMachineUtilisationKpiQueryHandler(db)
            .Handle(new GetMachineUtilisationKpiQuery(from, from), CancellationToken.None);

        result.Machines.Should().ContainSingle().Subject.UtilisationPct.Should().BeNull();
        result.OverallUtilisationPct.Should().BeNull();
    }

    // ---------- 4. Throughput ----------

    /// <summary>
    /// T1 100kg (stage A, shift 1), T2 200kg (stage B, shift 2), T3 50kg with
    /// no execution link (legacy null -&gt; "?"). ByStage: A=100, B=200, ?=50.
    /// ByShift: S1=100, S2=200, ?=50. Total 350kg, 3 transfers.
    /// </summary>
    [Fact]
    public async Task Throughput_GroupsByStageAndShift()
    {
        await using var db = new ApplicationDbContext(NewOptions("throughput"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.SingleAsync();
        var ready = await db.Warehouses.SingleAsync(w => w.Kind == WarehouseKind.ReadyGoods);
        var stageA = await db.ProductionStageDefinitions.SingleAsync();
        var stageB = new ProductionStageDefinition("PS002", "Dyeing", 2, "seed");
        db.ProductionStageDefinitions.Add(stageB);
        var shift1 = new ProductionShift("SHIFT-1", "First", 1, "seed", hoursPerDay: 8m);
        var shift2 = new ProductionShift("SHIFT-2", "Second", 2, "seed", hoursPerDay: 8m);
        db.AddRange(shift1, shift2);

        var date = new DateTime(2026, 4, 10);
        // The route starts with a single execution (the next operation is
        // chosen on the floor), so two orders give two stage links.
        var order1 = new ProductionOrder("PRD-T1", customer.Id, item.Id, date, "seed", requestedQuantityKg: 1000m);
        order1.BuildStageRoute(new[] { stageA, stageB }, "seed");
        var order2 = new ProductionOrder("PRD-T2", customer.Id, item.Id, date, "seed", requestedQuantityKg: 1000m);
        order2.BuildStageRoute(new[] { stageA, stageB }, "seed", startingStageId: stageB.Id);
        var execA = order1.StageExecutions.Should().ContainSingle().Subject;
        var execB = order2.StageExecutions.Should().ContainSingle().Subject;
        execB.StageDefinitionId.Should().Be(stageB.Id);
        db.ProductionOrders.AddRange(order1, order2);
        await db.SaveChangesAsync();

        void PostTransfer(ProductionOrder order, string number, decimal kg, Guid? executionId)
        {
            db.ReadyGoodsTransfers.Add(new ReadyGoodsTransfer(
                number, date, order.Id, customer.Id, item.Id, ready.Id, kg, null, "seed",
                stageExecutionId: executionId, category: ReadyGoodsCategory.FirstGrade));
        }

        // Tag executions to stages/shifts via CloseWithOutput (also makes them Completed).
        execA.CloseWithOutput(100m, null, null, null, null, "seed", inputKg: 100m, machineId: null, productionShiftId: shift1.Id);
        execB.CloseWithOutput(200m, null, null, null, null, "seed", inputKg: 200m, machineId: null, productionShiftId: shift2.Id);
        PostTransfer(order1, "RGT-1", 100m, execA.Id);
        PostTransfer(order2, "RGT-2", 200m, execB.Id);
        PostTransfer(order1, "RGT-3", 50m, null);
        await db.SaveChangesAsync();

        var result = await new GetThroughputKpiQueryHandler(db)
            .Handle(new GetThroughputKpiQuery(date.AddDays(-1), date.AddDays(1)), CancellationToken.None);

        result.TotalTransfers.Should().Be(3);
        result.TotalQuantityKg.Should().Be(350m);
        result.ByStage.Should().HaveCount(3);
        result.ByStage.Single(g => g.Code == "PS001").QuantityKg.Should().Be(100m);
        result.ByStage.Single(g => g.Code == "PS002").QuantityKg.Should().Be(200m);
        result.ByStage.Single(g => g.Code == "?").QuantityKg.Should().Be(50m);
        result.ByShift.Should().HaveCount(3);
        result.ByShift.Single(g => g.Code == "SHIFT-1").QuantityKg.Should().Be(100m);
        result.ByShift.Single(g => g.Code == "SHIFT-2").QuantityKg.Should().Be(200m);
        result.ByShift.Single(g => g.Code == "?").QuantityKg.Should().Be(50m);
    }

    // ---------- 5. Customer exposure ----------

    /// <summary>
    /// C-1: invoice 1000 (lines) with a 400 receipt -&gt; open 600; invoice 500
    /// with no receipt -&gt; open 500. Ledger debits 1000 + 500, credit 400
    /// -&gt; balance 1100. Open total 1100, oldest unpaid = INV-1 (20 days).
    /// C-2 has no activity and is excluded.
    /// </summary>
    [Fact]
    public async Task CustomerExposure_SumsLedgerAndAgesOldestInvoice()
    {
        await using var db = new ApplicationDbContext(NewOptions("exposure"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var idle = new Customer("C-2", "Idle", "seed");
        db.Customers.Add(idle);
        var item = await db.Items.SingleAsync();
        var account = new TreasuryAccount("CASH-1", "Cash", TreasuryAccountKind.Cash, "seed");
        db.TreasuryAccounts.Add(account);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        var inv1 = new Invoice("INV-1", today.AddDays(-20), customer.Id, "seed");
        inv1.AddLine(null, item.Id, null, quantity: 10m, processingPrice: 100m, unit: UnitOfMeasure.KG);
        inv1.Issue();
        var inv2 = new Invoice("INV-2", today.AddDays(-5), customer.Id, "seed");
        inv2.AddLine(null, item.Id, null, quantity: 5m, processingPrice: 100m, unit: UnitOfMeasure.KG);
        inv2.Issue();
        db.Invoices.AddRange(inv1, inv2);
        await db.SaveChangesAsync();

        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, today.AddDays(-20), DocumentType.Invoice, "INV-1", inv1.Id,
            debit: 1000m, credit: 0m, description: "Invoice INV-1", createdBy: "seed"));
        var receipt = new Receipt("RC-1", today.AddDays(-10), account.Id, 400m, "seed",
            customerId: customer.Id, invoiceId: inv1.Id);
        db.Receipts.Add(receipt);
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, today.AddDays(-10), DocumentType.Receipt, "RC-1", receipt.Id,
            debit: 0m, credit: 400m, description: "Receipt RC-1", createdBy: "seed"));
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, today.AddDays(-5), DocumentType.Invoice, "INV-2", inv2.Id,
            debit: 500m, credit: 0m, description: "Invoice INV-2", createdBy: "seed"));
        await db.SaveChangesAsync();

        var result = await new GetCustomerExposureKpiQueryHandler(db)
            .Handle(new GetCustomerExposureKpiQuery(), CancellationToken.None);

        var row = result.Customers.Should().ContainSingle().Subject;
        row.CustomerCode.Should().Be("C-1");
        row.OpenBalance.Should().Be(1100m);
        row.OpenInvoicesCount.Should().Be(2);
        row.OpenInvoicesTotal.Should().Be(1100m);
        row.OldestUnpaidInvoiceNumber.Should().Be("INV-1");
        row.OldestUnpaidInvoiceAgeDays.Should().Be(20);
        result.TotalOpenBalance.Should().Be(1100m);
        result.TotalOpenInvoices.Should().Be(1100m);
    }

    // ---------- 6. Cheques due ----------

    /// <summary>
    /// Window = 7 days from today: CHK-DUE (due +3, customer, 1000) and
    /// CHK-SUP (due +7, supplier, 500) are listed; CHK-FAR (due +30),
    /// CHK-CLEARED and CHK-CANCELLED are excluded. Total 1500, count 2.
    /// </summary>
    [Fact]
    public async Task ChequesDue_ListsOnlyUnclearedChequesInWindow()
    {
        await using var db = new ApplicationDbContext(NewOptions("cheques"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var supplier = new Supplier("S-1", "Supplier", "seed");
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.Date;
        Check Due(string number, DateTime due, CheckDirection dir, decimal amount, Guid? customerId = null, Guid? supplierId = null)
            => new(number, dir, "NBE", amount, today.AddDays(-30), due,
                issuer: "Issuer", originalHolder: "Holder", originalHolderType: CheckHolderType.Customer,
                createdBy: "seed", customerId: customerId, supplierId: supplierId);

        var due = Due("CHK-DUE", today.AddDays(3), CheckDirection.CustomerCheck, 1000m, customerId: customer.Id);
        var sup = Due("CHK-SUP", today.AddDays(7), CheckDirection.SupplierCheck, 500m, supplierId: supplier.Id);
        var far = Due("CHK-FAR", today.AddDays(30), CheckDirection.CustomerCheck, 200m, customerId: customer.Id);
        var cleared = Due("CHK-CLR", today.AddDays(2), CheckDirection.CustomerCheck, 300m, customerId: customer.Id);
        cleared.Deposit(Guid.NewGuid(), "NBE-001", today.AddDays(-1), "seed");
        cleared.Clear(today, "seed");
        var cancelled = Due("CHK-CXL", today.AddDays(2), CheckDirection.CustomerCheck, 400m, customerId: customer.Id);
        cancelled.Cancel(today, "seed", "mistake");
        db.Checks.AddRange(due, sup, far, cleared, cancelled);
        await db.SaveChangesAsync();

        var result = await new GetChequesDueKpiQueryHandler(db)
            .Handle(new GetChequesDueKpiQuery(7), CancellationToken.None);

        result.ChequesCount.Should().Be(2);
        result.TotalAmount.Should().Be(1500m);
        var row = result.Cheques.Single(c => c.CheckNumber == "CHK-DUE");
        row.Kind.Should().Be("Customer");
        row.PartyCode.Should().Be("C-1");
        row.DaysUntilDue.Should().Be(3);
        result.Cheques.Single(c => c.CheckNumber == "CHK-SUP").Kind.Should().Be("Supplier");
    }

    // ---------- 7. Governance ----------

    /// <summary>
    /// One override approved now and one audit-log reopening entry in range are
    /// both listed with count 1; an older audit entry outside the range is
    /// ignored.
    /// </summary>
    [Fact]
    public async Task Governance_ListsOverridesAndAuditReopenings()
    {
        await using var db = new ApplicationDbContext(NewOptions("gov"));
        await SeedAsync(db);
        var customer = await db.Customers.SingleAsync();
        var item = await db.Items.SingleAsync();
        var warehouse = await db.Warehouses.FirstAsync();
        var message = new RawMessage("RMSG-1", DateTime.UtcNow, customer.Id, warehouse.Id, "seed", "seed");
        db.RawMessages.Add(message);
        await db.SaveChangesAsync();

        db.NegativeStockOverrides.Add(new NegativeStockOverride(
            message.Id, item.Id, customer.Id, requestedQuantity: 5m, balanceBefore: 10m,
            reason: "Urgent order", requestedBy: "op", approvedBy: "mgr"));

        var period = new PeriodClose(DateTime.UtcNow.Date.AddDays(-60), DateTime.UtcNow.Date.AddDays(-31), "seed");
        db.PeriodCloses.Add(period);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.AuditLogEntries.Add(new AuditLogEntry(
            now.AddDays(-2), "mgr", "Update", "PeriodClose", period.Id.ToString(),
            beforeDataJson: null, afterDataJson: null, ipAddress: null, reason: "Reopen for correction"));
        db.AuditLogEntries.Add(new AuditLogEntry(
            now.AddDays(-90), "mgr", "Update", "PeriodClose", period.Id.ToString(),
            beforeDataJson: null, afterDataJson: null, ipAddress: null, reason: "Old"));
        await db.SaveChangesAsync();

        var result = await new GetGovernanceKpiQueryHandler(db)
            .Handle(new GetGovernanceKpiQuery(now.AddDays(-30), now.AddDays(1)), CancellationToken.None);

        result.OverridesCount.Should().Be(1);
        result.Overrides.Should().ContainSingle().Subject.MessageNumber.Should().Be("RMSG-1");
        result.ReopeningsCount.Should().Be(1);
        var reopening = result.Reopenings.Should().ContainSingle().Subject;
        reopening.PeriodCloseId.Should().Be(period.Id);
        reopening.AuditUserName.Should().Be("mgr");
    }
}

/// <summary>
/// Accounting export tests (spec Part 2): CSV bytes, totals consistency and
/// the audit entry contract.
/// </summary>
public class AccountingExportTests
{
    private DbContextOptions<ApplicationDbContext> NewOptions(string name)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"acct-export-{name}-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private sealed class FakeUser : ICurrentUserService
    {
        public string UserName => "accountant";
        public Guid? UserId => null;
        public string? IpAddress => null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
    }

    private sealed class FakeClock : IDateTime
    {
        public DateTime UtcNow { get; } = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    }

    private sealed class StubIdentity : ICompanyIdentityProvider
    {
        public CompanyIdentity Get() => new("مصنع", "Factory", null);
    }

    private static ExportAccountingQueryHandler NewHandler(ApplicationDbContext db)
        => new(db,
            new Infrastructure.Services.ReportExportService(new StubIdentity()),
            new FakeClock(),
            new FakeUser());

    /// <summary>
    /// Customer invoice 1000 (10 x 100) issued Jan 10 with its ledger debit
    /// row, plus a 500 treasury receipt pair. The CSV must open with a UTF-8
    /// BOM, the line-total column must sum to 1000, the invoice-total cell in
    /// the totals row must equal the independently recomputed total, and the
    /// audit entry must record range/format/row counts without file contents.
    /// </summary>
    [Fact]
    public async Task CustomerInvoicesCsv_TotalsMatchIndependentRecomputation_AndAuditsWithoutContents()
    {
        await using var db = new ApplicationDbContext(NewOptions("csv"));
        var customer = new Customer("C-1", "Customer One", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        db.AddRange(customer, item);
        await db.SaveChangesAsync();

        var invoiceDate = new DateTime(2026, 1, 10);
        var invoice = new Invoice("INV-1", invoiceDate, customer.Id, "seed");
        invoice.AddLine(null, item.Id, null, quantity: 10m, processingPrice: 100m, unit: UnitOfMeasure.KG);
        invoice.Issue();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        db.CustomerLedgerEntries.Add(new CustomerLedgerEntry(
            customer.Id, invoiceDate, DocumentType.Invoice, "INV-1", invoice.Id,
            debit: 1000m, credit: 0m, description: "Invoice INV-1", createdBy: "seed"));
        await db.SaveChangesAsync();

        var file = await NewHandler(db).Handle(
            new ExportAccountingQuery(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31),
                Format: "csv", Set: AccountingExportSets.CustomerInvoices),
            CancellationToken.None);

        file.Content[0].Should().Be(0xEF);
        file.Content[1].Should().Be(0xBB);
        file.Content[2].Should().Be(0xBF);
        file.ContentType.Should().Be("text/csv");
        file.SetsIncluded.Should().Equal(AccountingExportSets.CustomerInvoices);
        file.TotalRows.Should().Be(1);

        var text = System.Text.Encoding.UTF8.GetString(file.Content);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3); // header + 1 line row + totals row
        lines[0].Should().Contain("InvoiceTotal");
        lines[1].Should().Contain("INV-1");
        lines[2].Should().StartWith("TOTAL");

        // Independent recomputation straight from the documents.
        var expectedLineTotal = await db.InvoiceLines.SumAsync(l => l.Quantity * l.ProcessingPrice);
        expectedLineTotal.Should().Be(1000m);
        var totals = lines[2].Split(',');
        totals[9].Should().Be("1000");   // LineTotal column
        totals[13].Should().Be("1000");  // InvoiceTotal column (distinct invoices)

        var audit = await db.AuditLogEntries.OrderByDescending(a => a.OccurredAtUtc).FirstAsync();
        audit.Action.Should().Be("AccountingExport");
        audit.EntityName.Should().Be("AccountingExport");
        audit.AfterDataJson.Should().Contain("2026-01-01").And.Contain("csv").And.Contain("customer-invoices");
        audit.AfterDataJson.Should().NotContain("INV-1");
    }

    /// <summary>
    /// Account history: +1000 before the range (opening), +500 In and -200 Out
    /// inside: exported running balances must be 1500 then 1300, and the
    /// totals row must equal the exported column sums (In 500, Out 200).
    /// </summary>
    [Fact]
    public async Task TreasuryCsv_RunningBalanceCompoundsFullHistory()
    {
        await using var db = new ApplicationDbContext(NewOptions("treasury"));
        var account = new TreasuryAccount("CASH-1", "Cash", TreasuryAccountKind.Cash, "seed");
        db.TreasuryAccounts.Add(account);
        await db.SaveChangesAsync();

        void Post(DateTime date, string number, decimal amount, TreasuryDirection direction)
            => db.TreasuryTransactions.Add(new TreasuryTransaction(
                account.Id, date, DocumentType.Receipt, number, Guid.NewGuid(),
                amount, direction, description: number, createdBy: "seed"));

        Post(new DateTime(2025, 12, 20), "PRE-1", 1000m, TreasuryDirection.In);
        Post(new DateTime(2026, 2, 5), "T-IN", 500m, TreasuryDirection.In);
        Post(new DateTime(2026, 2, 10), "T-OUT", 200m, TreasuryDirection.Out);
        await db.SaveChangesAsync();

        var file = await NewHandler(db).Handle(
            new ExportAccountingQuery(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28),
                Format: "csv", Set: AccountingExportSets.Treasury),
            CancellationToken.None);

        file.TotalRows.Should().Be(2);
        var lines = System.Text.Encoding.UTF8.GetString(file.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4); // header + 2 rows + totals
        lines[1].Should().Contain("1500"); // 1000 opening + 500
        lines[2].Should().Contain("1300"); // 1500 - 200
        var totals = lines[3].Split(',');
        totals[0].Should().Be("TOTAL");
        totals[5].Should().Be("500");  // InAmount
        totals[6].Should().Be("200");  // OutAmount
    }

    /// <summary>
    /// XLSX with no set exports all five sheets in one workbook and the audit
    /// entry records every set with its row count.
    /// </summary>
    [Fact]
    public async Task XlsxExport_ContainsAllSheets_AndAuditsRowCounts()
    {
        await using var db = new ApplicationDbContext(NewOptions("xlsx"));
        var customer = new Customer("C-1", "Customer One", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        db.AddRange(customer, item);
        await db.SaveChangesAsync();

        var file = await NewHandler(db).Handle(
            new ExportAccountingQuery(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), Format: "xlsx"),
            CancellationToken.None);

        file.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        file.Content.Should().NotBeEmpty();
        file.SetsIncluded.Should().BeEquivalentTo(AccountingExportSets.All);
        file.RowsPerSet.Should().HaveCount(5);
        file.TotalRows.Should().Be(file.RowsPerSet.Values.Sum());

        var audit = await db.AuditLogEntries.OrderByDescending(a => a.OccurredAtUtc).FirstAsync();
        audit.AfterDataJson.Should().Contain("xlsx").And.Contain("treasury");
    }

    /// <summary>
    /// The summary composes the seven KPIs through the real mediator pipeline:
    /// the waste figure here must equal the waste detail query for the same
    /// window, so the dashboard can never disagree with the detail screens.
    /// </summary>
    [Fact]
    public async Task KpiSummary_MatchesDetailQuery()
    {
        var options = NewOptions("summary");
        await using var setup = new ApplicationDbContext(options);
        var customer = new Customer("C-1", "Customer One", "seed");
        var item = new Item("IT-1", "Item", UnitOfMeasure.KG, "seed");
        var formation = new ProductionStageDefinition("PS001", "Formation", 1, "seed", isFormationStage: true);
        setup.AddRange(customer, item, formation);
        await setup.SaveChangesAsync();

        var order = new ProductionOrder("PRD-001", customer.Id, item.Id, new DateTime(2026, 3, 1), "seed", requestedQuantityKg: 1000m);
        order.BuildStageRoute(new[] { formation }, "seed");
        order.StageExecutions.Single().CloseWithOutput(
            outputKg: 900m, outputMeter: null, separatesKg: 20m, separatesMeter: null,
            notes: null, completedBy: "seed", inputKg: 1000m, secondGradeKg: 50m);
        setup.ProductionOrders.Add(order);
        await setup.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetWasteRateKpiQuery>());
        await using var db = new ApplicationDbContext(options);
        services.AddSingleton<IApplicationDbContext>(db);
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var from = new DateTime(2026, 3, 1);
        var to = new DateTime(2026, 3, 31);
        var summary = await sender.Send(new GetKpiSummaryQuery(from, to));
        var detail = await sender.Send(new GetWasteRateKpiQuery(from, to));

        summary.OverallWasteRatePct.Should().Be(detail.OverallRatePct);
        summary.TotalWasteKg.Should().Be(detail.TotalWasteKg);
        summary.From.Should().Be(from);
        summary.TimeZone.Should().Be("UTC");
    }
}
