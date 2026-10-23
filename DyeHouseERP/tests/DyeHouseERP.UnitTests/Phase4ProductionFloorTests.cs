using System.Reflection;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.Separates.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 4 - the production floor: what a closing operation actually recorded.
///
/// The transfer IS the end of an operation (spec sections 13-17), so this is the one
/// transaction that has to capture the floor's real figures: the ACTUAL input (never
/// inferred from the order's requested quantity), the separates (reprocessable material,
/// explicitly not waste) and the second-grade quantity (a figure of its own, neither
/// output nor waste). Every figure belongs to the operation being closed and is validated
/// against the item's own unit configuration - KG and Meter are never converted.
///
/// Two consequences are pinned here, because both were previously unreachable:
///   * a separates quantity recorded on a closing operation ENTERS the separates register
///     (a <see cref="Separate"/> row), which is what the register, the reprocessing decision
///     and the scrap decision all read from;
///   * because material can therefore exist in the register, the scrap/reprocess lifecycle
///     is exercised end to end, including the Phase 4 decision that scrapping is its own
///     permission (<c>production.scrap</c>) rather than an implied <c>production.edit</c>.
/// </summary>
public class Phase4FloorCaptureTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public Phase4FloorCaptureTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase4-floor-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static TransferToNextStageCommandHandler Transfer(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedClock(), new OpenPeriod(), new FixedNumberGenerator());

    private static ScrapSeparateCommandHandler Scrapper(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"));

    private static ReprocessSeparateCommandHandler Reprocessor(ApplicationDbContext db) =>
        new(db, new StubCurrentUser("operator"), new FixedNumberGenerator());

    /// <summary>
    /// A fully-allocated Job Order on its first (formation) operation, ready to be transferred
    /// to a second operation. The item's unit configuration is chosen by the test, because the
    /// whole point of the guard is that the ITEM decides which units may be recorded.
    /// </summary>
    private sealed record Scene(
        Guid OrderId, Guid CustomerId, Guid ItemId, Guid FirstExecutionId, Guid NextStageId, Guid FormationId);

    private static Scene SeedOrderOnFirstStage(ApplicationDbContext db, UnitOfMeasure baseUnit,
        UnitOfMeasure? secondaryUnit = null, bool requireBothUnits = false)
    {
        var customer = new Customer("C-P4", "Customer", "seed");
        var item = new Item("IT-P4", "Item", baseUnit, "seed",
            secondaryUnit: secondaryUnit, requireBothUnits: requireBothUnits);
        var raw = new Warehouse("RAW", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var formation = new ProductionStageDefinition("PS001", "التشكيل", 1, "seed", isFormationStage: true);
        var next = new ProductionStageDefinition("PS002", "التجهيز", 2, "seed");

        var message = new RawMessage("RMSG-P4", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        message.AddLine(item.Id, 100_000m, 100_000m, null, null);

        db.AddRange(customer, item, raw, formation, next, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 100_000m, quantityMeter: 100_000m, direction: TransactionDirection.In, createdBy: "seed"));

        var order = new ProductionOrder("PRD-P4-000001", customer.Id, item.Id, DateTime.UtcNow, "seed",
            requestedQuantityKg: 1000m, requestedQuantityMeter: 1000m);
        db.ProductionOrders.Add(order);
        order.BuildStageRoute(new[] { formation }, "seed");
        db.RawAllocations.Add(order.AllocateRaw(message.Id, item.Id, 1000m, 1000m, "seed"));
        db.SaveChanges();

        var first = db.ProductionOrderStageExecutions.Local.Single(s => s.ProductionOrderId == order.Id);
        return new Scene(order.Id, customer.Id, item.Id, first.Id, next.Id, formation.Id);
    }

    private static TransferToNextStageCommand CloseFirstOperation(
        Scene scene, decimal? inputKg = null, decimal? inputMeter = null,
        decimal? outputKg = null, decimal? outputMeter = null,
        decimal? separatesKg = null, decimal? separatesMeter = null,
        decimal? secondGradeKg = null, decimal? secondGradeMeter = null) =>
        new(scene.FirstExecutionId, scene.NextStageId, outputKg, outputMeter, separatesKg, separatesMeter,
            Notes: "floor entry", DrawingNumber: null,
            InputKg: inputKg, InputMeter: inputMeter,
            SecondGradeKg: secondGradeKg, SecondGradeMeter: secondGradeMeter);

    // =====================================================================================
    // 1. The figures a closing operation records are recorded as entered
    // =====================================================================================

    [Fact]
    public async Task AClosingOperation_RecordsItsActualInputSeparatesAndSecondGrade_AsEntered()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG, secondaryUnit: UnitOfMeasure.Meter);

        await Transfer(db).Handle(CloseFirstOperation(scene,
            inputKg: 1010m, inputMeter: 1005m,
            outputKg: 950m, outputMeter: 940m,
            separatesKg: 15m, separatesMeter: 12m,
            secondGradeKg: 20m, secondGradeMeter: 18m), CancellationToken.None);

        var closed = await db.ProductionOrderStageExecutions.AsNoTracking()
            .SingleAsync(s => s.Id == scene.FirstExecutionId);

        closed.Status.Should().Be(StageExecutionStatus.Completed);
        closed.InputKg.Should().Be(1010m, "the ACTUAL input is recorded as entered - it is never inferred from the requested quantity");
        closed.InputMeter.Should().Be(1005m);
        closed.OutputKg.Should().Be(950m);
        closed.SeparatesKg.Should().Be(15m, "separates are recorded on the operation that produced them");
        closed.SecondGradeKg.Should().Be(20m, "second grade is a quantity of its own, recorded beside output and waste");
        closed.SecondGradeMeter.Should().Be(18m);

        // The waste is measured from the immutable baseline and the output ONLY: neither the
        // separates nor the second-grade quantity is subtracted from it (each is reported
        // separately), and the KG figure never touches the Meter figure.
        closed.LossKg.Should().Be(50m, "baseline 1000 - output 950; separates and second grade are not waste");
        closed.LossMeter.Should().Be(60m, "baseline 1000 - output 940, computed independently of KG");
    }

    // =====================================================================================
    // 2. Every recorded figure obeys the ITEM's own unit configuration
    // =====================================================================================

    [Fact]
    public async Task SecondGradeOrSeparates_InAUnitTheItemDoesNotUse_IsRefused_WithNothingWritten()
    {
        using var db = NewDb();
        // A KG-only item: Meter is not one of its units, so no Meter figure may be recorded for it.
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);

        Func<Task> meterSecondGrade = () => Transfer(db).Handle(CloseFirstOperation(scene,
            outputKg: 950m, secondGradeMeter: 18m), CancellationToken.None);
        (await meterSecondGrade.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*",
                "a second-grade Meter quantity cannot be posted for a KG-only item - there is no conversion");

        Func<Task> meterSeparates = () => Transfer(db).Handle(CloseFirstOperation(scene,
            outputKg: 950m, separatesMeter: 12m), CancellationToken.None);
        (await meterSeparates.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");

        var stillOpen = await db.ProductionOrderStageExecutions.AsNoTracking()
            .SingleAsync(s => s.Id == scene.FirstExecutionId);
        stillOpen.Status.Should().Be(StageExecutionStatus.InProgress, "a refused transfer closes no operation");
        stillOpen.OutputKg.Should().BeNull("nothing is recorded by a refused transfer");
        (await db.Separates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnItemThatRequiresBothUnits_NeedsTheActualInputInBoth()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG,
            secondaryUnit: UnitOfMeasure.Meter, requireBothUnits: true);

        // Output in both units (so the transfer itself is well-formed) but the actual input in KG only:
        // the item that requires both measurements does not accept a half-recorded operation.
        Func<Task> act = () => Transfer(db).Handle(CloseFirstOperation(scene,
            inputKg: 1010m, outputKg: 950m, outputMeter: 940m), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*requires BOTH of its units*");
    }

    // =====================================================================================
    // 3. Recording separates ENTERS the register (the missing link)
    // =====================================================================================

    [Fact]
    public async Task ARecordedSeparatesQuantity_EntersTheSeparatesRegister_LinkedToItsOrderAndOperation()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);

        await Transfer(db).Handle(CloseFirstOperation(scene,
            outputKg: 950m, separatesKg: 15m), CancellationToken.None);

        var separate = await db.Separates.AsNoTracking().SingleAsync();

        separate.OriginalProductionOrderId.Should().Be(scene.OrderId);
        separate.StageExecutionId.Should().Be(scene.FirstExecutionId,
            "the register keeps the operation the material came off");
        separate.CustomerId.Should().Be(scene.CustomerId);
        separate.ItemId.Should().Be(scene.ItemId);
        separate.QuantityKg.Should().Be(15m);
        separate.Status.Should().Be(SeparateStatus.PendingReprocessing,
            "separates are reprocessable material - explicitly not loss/waste");
        separate.ReprocessingProductionOrderId.Should().BeNull();
    }

    [Fact]
    public async Task AnOperationThatRecordedNoSeparates_AddsNothingToTheRegister()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);

        await Transfer(db).Handle(CloseFirstOperation(scene,
            inputKg: 1010m, outputKg: 950m, secondGradeKg: 20m), CancellationToken.None);

        (await db.Separates.CountAsync()).Should().Be(0,
            "a zero/absent separates quantity creates no register row - only actual separates are tracked");
    }

    // =====================================================================================
    // 4. Scrap: its own decision, its own permission, once only
    // =====================================================================================

    [Fact]
    public async Task ScrappingASeparate_RequiresAReason_AndCanHappenOnlyOnce()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);
        await Transfer(db).Handle(CloseFirstOperation(scene, outputKg: 950m, separatesKg: 15m), CancellationToken.None);
        var separateId = (await db.Separates.AsNoTracking().SingleAsync()).Id;

        new ScrapSeparateCommandValidator().Validate(new ScrapSeparateCommand(separateId, "   ")).IsValid
            .Should().BeFalse("writing material off as waste must state why");
        new ScrapSeparateCommandValidator().Validate(new ScrapSeparateCommand(separateId, "تلطخ لا يمكن إعادة تشغيله")).IsValid
            .Should().BeTrue();

        var scrapped = await Scrapper(db).Handle(
            new ScrapSeparateCommand(separateId, "تلطخ لا يمكن إعادة تشغيله"), CancellationToken.None);

        scrapped.Status.Should().Be(SeparateStatus.Scrapped);
        scrapped.Notes.Should().Contain("تلطخ لا يمكن إعادة تشغيله");

        Func<Task> again = () => Scrapper(db).Handle(
            new ScrapSeparateCommand(separateId, "مرة أخرى"), CancellationToken.None);

        await again.Should().ThrowAsync<DomainException>(
            "an already-decided separate can never be written off twice");
    }

    [Fact]
    public async Task ASeparateAlreadyAssignedToReprocessing_CannotAlsoBeScrapped()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);
        await Transfer(db).Handle(
            CloseFirstOperation(scene, inputKg: 1010m, outputKg: 950m, separatesKg: 15m),
            CancellationToken.None);
        var separate = await db.Separates.AsNoTracking().SingleAsync();

        await Reprocessor(db).Handle(
            new ReprocessSeparateCommand(separate.Id, DateTime.UtcNow, "إعادة تشغيل"),
            CancellationToken.None);

        Func<Task> scrap = () => Scrapper(db).Handle(
            new ScrapSeparateCommand(separate.Id, "إلغاء المنفصل وتحويله لهالك"),
            CancellationToken.None);

        (await scrap.Should().ThrowAsync<DomainException>())
            .WithMessage("*cannot be scrapped*");

        var unchanged = await db.Separates.AsNoTracking().SingleAsync(s => s.Id == separate.Id);
        unchanged.Status.Should().Be(SeparateStatus.Reprocessing);
        unchanged.ReprocessingProductionOrderId.Should().NotBeNull();
    }

    [Fact]
    public void ScrappingASeparate_IsItsOwnConfigurableRight_NotAnImpliedEditRight()
    {
        // Phase 4 decided the open question from Phase 1 §G.1: the permission the spec's checklist
        // names is real, so granting someone the ability to edit a Job Order never silently grants
        // them the authority to write material off.
        Permissions.ProductionScrap.Should().Be("production.scrap");
        Permissions.All.Should().Contain(Permissions.ProductionScrap);
        Permissions.ProductionScrap.Should().NotBe(Permissions.ProductionEdit);

        var policies = typeof(SeparatesController).GetMethod(nameof(SeparatesController.Scrap))!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Where(p => p is not null).ToList();

        policies.Should().Contain(PermissionPolicy.Prefix + Permissions.ProductionScrap);
        policies.Should().NotContain(PermissionPolicy.Prefix + Permissions.ProductionEdit,
            "scrapping is not an ordinary Job Order edit");

        typeof(SeparatesController).GetMethod(nameof(SeparatesController.Scrap))!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().NotContain(a => a.Roles != null,
                "a role gate would make the granular permission unusable for a non-admin user");
    }

    // =====================================================================================
    // 5. Reprocessing: a NEW order, linked backwards, original history untouched
    // =====================================================================================

    [Fact]
    public async Task ReprocessingASeparate_CreatesANewLinkedOrder_AndLeavesTheOriginalUntouched()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);
        await Transfer(db).Handle(CloseFirstOperation(scene,
            inputKg: 1010m, outputKg: 950m, separatesKg: 15m), CancellationToken.None);
        var separate = await db.Separates.AsNoTracking().SingleAsync();

        var newOrder = await Reprocessor(db).Handle(
            new ReprocessSeparateCommand(separate.Id, DateTime.UtcNow, "إعادة تشغيل المنفصلات"), CancellationToken.None);

        newOrder.CustomerId.Should().Be(scene.CustomerId);
        newOrder.ItemId.Should().Be(scene.ItemId);
        newOrder.RequestedQuantityKg.Should().Be(15m, "the reprocessing order is raised on the separate's own quantity");
        newOrder.ReprocessingOfProductionOrderId.Should().Be(scene.OrderId,
            "genealogy is preserved by pointing back - the original order is never edited");

        var stored = await db.Separates.AsNoTracking().SingleAsync(s => s.Id == separate.Id);
        stored.Status.Should().Be(SeparateStatus.Reprocessing);
        stored.ReprocessingProductionOrderId.Should().Be(newOrder.Id);

        var original = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == scene.OrderId);
        original.OrderNumber.Should().Be("PRD-P4-000001");
        original.RequestedQuantityKg.Should().Be(1000m, "reprocessing never rewrites the original order's figures");
        (await db.ProductionOrderStageExecutions.CountAsync(s => s.ProductionOrderId == scene.OrderId))
            .Should().Be(2, "the original order keeps its own operations");
    }

    [Fact]
    public async Task ReprocessingASeparate_WhoseUnitTheItemDoesNotUse_IsRefused()
    {
        using var db = NewDb();
        var scene = SeedOrderOnFirstStage(db, UnitOfMeasure.KG);

        // A Meter quantity exists on the register for a KG-only item (recorded before the item's
        // configuration was tightened, or by any other path). The reprocessing order must refuse it
        // rather than convert it into KG.
        var separate = new Separate(scene.OrderId, scene.FirstExecutionId, scene.CustomerId, scene.ItemId,
            quantityKg: null, quantityMeter: 5m, createdBy: "seed");
        db.Separates.Add(separate);
        await db.SaveChangesAsync();

        Func<Task> act = () => Reprocessor(db).Handle(
            new ReprocessSeparateCommand(separate.Id, DateTime.UtcNow, null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("*not configured to use Meter*");

        (await db.Separates.AsNoTracking().SingleAsync(s => s.Id == separate.Id))
            .Status.Should().Be(SeparateStatus.PendingReprocessing, "a refused reprocessing changes nothing");
        (await db.ProductionOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ScrappingOrReprocessingAnUnknownSeparate_IsRefused()
    {
        using var db = NewDb();
        SeedOrderOnFirstStage(db, UnitOfMeasure.KG);

        Func<Task> scrap = () => Scrapper(db).Handle(
            new ScrapSeparateCommand(Guid.NewGuid(), "سبب"), CancellationToken.None);
        Func<Task> reprocess = () => Reprocessor(db).Handle(
            new ReprocessSeparateCommand(Guid.NewGuid(), DateTime.UtcNow, null), CancellationToken.None);

        await scrap.Should().ThrowAsync<NotFoundException>();
        await reprocess.Should().ThrowAsync<NotFoundException>();
    }

    // =====================================================================================
    // 6. Reprocessing never deducts the original raw material a second time
    // =====================================================================================

    /// <summary>
    /// The direct proof the earlier phases lacked (Phase 9 recorded it as a coverage gap).
    ///
    /// Reprocessing a separate raises a NEW Job Order that points back at the original; it must
    /// NOT re-issue the original order's raw material. The inventory ledger is the record of every
    /// raw deduction, so the test compares it row-for-row before and after the reprocessing: the
    /// original order's OUT rows must be identical and the ledger must gain no row at all. The
    /// reprocessing order's claim is the separate's own quantity (15 kg), never the original 1000.
    /// </summary>
    [Fact]
    public async Task ReprocessingASeparate_PostsNoSecondRawDeduction_AndLeavesTheOriginalIssueIntact()
    {
        using var db = NewDb();

        // A REAL raw issue creates the original order: one OUT ledger row of 1000 kg from the
        // customer's lot, and the order's opening quantity is the ACTUAL issued figure.
        var customer = new Customer("C-P4R", "Customer", "seed");
        var item = new Item("IT-P4R", "Item", UnitOfMeasure.KG, "seed");
        var raw = new Warehouse("RAWR", "Raw Material", WarehouseKind.RawMaterial, "seed");
        var formation = new ProductionStageDefinition("PSR1", "التشكيل", 1, "seed", isFormationStage: true);
        var next = new ProductionStageDefinition("PSR2", "التجهيز", 2, "seed");
        var message = new RawMessage("RMSG-P4R", DateTime.UtcNow, customer.Id, raw.Id, "seed", "seed");
        message.AddLine(item.Id, 100_000m, null, null, null);
        db.AddRange(customer, item, raw, formation, next, message);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.RawReceiptMessage, message.MessageNumber, message.Id, DateTime.UtcNow,
            raw.Id, customer.Id, item.Id, rawMessageId: message.Id, productionOrderId: null,
            quantityKg: 100_000m, quantityMeter: null, direction: TransactionDirection.In, createdBy: "seed"));
        await db.SaveChangesAsync();

        var issued = await new IssueRawMaterialCommandHandler(
                db, new StubCurrentUser("operator"), new InventoryLedgerService(db), new FixedNumberGenerator(),
                new FixedClock(), new OpenPeriod(), new RecordingStockLock())
            .Handle(new IssueRawMaterialCommand(customer.Id, item.Id, message.Id, DateTime.UtcNow,
                ActualQuantityKg: 1000m, ActualQuantityMeter: null, Notes: "raw issue"), CancellationToken.None);

        var firstExecutionId = await db.ProductionOrderStageExecutions
            .Where(s => s.ProductionOrderId == issued.Id).Select(s => s.Id).FirstAsync();

        // Close the formation operation and record separates, so a Separate enters the register.
        await Transfer(db).Handle(
            new TransferToNextStageCommand(firstExecutionId, next.Id, 950m, null, 15m, null,
                Notes: "floor entry", DrawingNumber: null, InputKg: 1010m, InputMeter: null,
                SecondGradeKg: null, SecondGradeMeter: null), CancellationToken.None);

        var separate = await db.Separates.AsNoTracking().SingleAsync();

        // --- BEFORE: the original order's raw-deduction rows, and the whole ledger size ----------
        var deductionsBefore = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductionOrderId == issued.Id && t.Direction == TransactionDirection.Out)
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.QuantityKg, t.QuantityMeter, t.SourceDocumentType, t.SourceDocumentNumber })
            .ToListAsync();

        deductionsBefore.Should().ContainSingle("the original order has exactly one raw deduction");
        deductionsBefore.Single().QuantityKg.Should().Be(1000m);
        var ledgerRowsBefore = await db.InventoryTransactions.CountAsync();

        // --- Reprocess the separate into a NEW linked order -------------------------------------
        var newOrder = await Reprocessor(db).Handle(
            new ReprocessSeparateCommand(separate.Id, DateTime.UtcNow, "إعادة تشغيل المنفصلات"), CancellationToken.None);

        // --- AFTER: comparable ledger -----------------------------------------------------------
        (await db.InventoryTransactions.CountAsync()).Should().Be(ledgerRowsBefore,
            "reprocessing only raises a new linked order - it must post NO inventory movement at all");

        var deductionsAfter = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductionOrderId == issued.Id && t.Direction == TransactionDirection.Out)
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.QuantityKg, t.QuantityMeter, t.SourceDocumentType, t.SourceDocumentNumber })
            .ToListAsync();

        deductionsAfter.Should().BeEquivalentTo(deductionsBefore,
            "the original order's raw deduction is byte-for-byte unchanged - never deducted again");

        // The reprocessing order owns no raw deduction, and its claim is the separate's own 15 kg.
        (await db.InventoryTransactions.CountAsync(t => t.ProductionOrderId == newOrder.Id))
            .Should().Be(0, "raising the reprocessing order must not invent a raw issue for it");
        newOrder.RequestedQuantityKg.Should().Be(15m, "the new order is sized by the separate, not the original 1000 kg");

        // The lot balance is exactly what the ONE original issue left - reprocessing consumed nothing.
        var lotBalanceKg = db.InventoryTransactions.AsNoTracking()
            .Where(t => t.RawMessageId == message.Id)
            .AsEnumerable()
            .Sum(t => (t.QuantityKg ?? 0m) * (t.Direction == TransactionDirection.In ? 1m : -1m));
        lotBalanceKg.Should().Be(100_000m - 1000m,
            "only the original 1000 kg issue deducted stock - reprocessing is not a second deduction");
    }
}
