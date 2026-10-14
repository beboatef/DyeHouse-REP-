using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Application.ProductionOrders.Commands;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Application.PriceLists.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class PrintingWorkflowTests
{
    private static ProductionStageDefinition Printing() => new("PRINTING", "Printing", 8, "seed", requiresDrawingNumber: true);
    private static ProductionOrder Order() => new("JOB-PRINT", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "seed", requestedQuantityKg: 100m);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Activation_Requires_A_Valid_Drawing(string? number)
    {
        var order = Order();
        var act = () => order.ActivateNextStage(Printing(), 100m, null, "operator", number);
        act.Should().Throw<DomainException>();
        order.StageExecutions.Should().BeEmpty();
    }

    [Fact]
    public void Drawing_Is_Trimmed_And_A_Closed_Stage_Is_Only_Corrected_With_A_Reason()
    {
        var stage = Order().ActivateNextStage(Printing(), 100m, null, "operator", " DR-123 ");
        stage.DrawingNumber.Should().Be("DR-123");
        stage.CloseWithOutput(95m, null, null, null, null, "operator");

        // A closed run is never SILENTLY rewritten: without a reason the correction is refused...
        var silent = () => stage.SetDrawingNumber("OTHER");
        silent.Should().Throw<DomainException>();
        stage.DrawingNumber.Should().Be("DR-123");
    }

    [Fact]
    public void A_Closed_Print_Run_Can_Be_Corrected_With_A_Reason()
    {
        var stage = Order().ActivateNextStage(Printing(), 100m, null, "operator", "DR-124");
        stage.CloseWithOutput(95m, null, null, null, null, "operator");

        // The drawing number is documentary: correcting a typo after the run closed must be possible.
        stage.SetDrawingNumber("DR-142", correctionReason: "typo on the drawing number");
        stage.DrawingNumber.Should().Be("DR-142");
    }

    [Fact]
    public async Task Drawing_Persists_Across_Transfers_And_Partial_Deliveries_And_Is_Passed_To_Pdf()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var printing = Printing();
        var finishing = new ProductionStageDefinition("FINISH", "Finishing", 2, "seed");
        var user = Mock.Of<ICurrentUserService>(u => u.UserName == "operator");
        ProductionOrder order;
        ProductionOrderStageExecution stage;
        await using (var db = new ApplicationDbContext(options))
        {
            // The item must exist: the stage-transfer handler validates the recorded
            // output against the item's CONFIGURED units (spec sections 5-6), so an
            // order pointing at a non-existent item is refused.
            var item = new Item("IT-PRINT", "Print Item", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
            order = new ProductionOrder("JOB-PRINT", Guid.NewGuid(), item.Id, DateTime.UtcNow, "seed", requestedQuantityKg: 100m);
            stage = order.ActivateNextStage(printing, 100m, null, "operator", "DR-123");
            db.Items.Add(item);
            db.ProductionStageDefinitions.AddRange(printing, finishing);
            db.ProductionOrders.Add(order);
            db.RawAllocations.Add(order.AllocateRaw(Guid.NewGuid(), order.ItemId, 100m, null, "operator"));
            await db.SaveChangesAsync();
            await new TransferToNextStageCommandHandler(db, user, new FixedClock(), new OpenPeriod(), new FixedNumberGenerator())
                .Handle(new(stage.Id, finishing.Id, 95m, null, null, null, null), default);
        }
        await using var verify = new ApplicationDbContext(options);
        var dto = await new GetProductionOrderByIdQueryHandler(verify).Handle(new(order.Id), default);
        dto.StageExecutions.Single(s => s.StageDefinitionId == printing.Id).DrawingNumber.Should().Be("DR-123");
        dto.StageExecutions.Single(s => s.StageDefinitionId == finishing.Id).DrawingNumber.Should().BeNull();
        var finishingExecution = dto.StageExecutions.Single(s => s.StageDefinitionId == finishing.Id);
        await new TransferToNextStageCommandHandler(verify, user, new FixedClock(), new OpenPeriod(), new FixedNumberGenerator())
            .Handle(new(finishingExecution.Id, printing.Id, 90m, null, null, null, null), default);
        verify.ProductionOrderStageExecutions.Where(s => s.ProductionOrderId == order.Id && s.RequiresDrawingNumber)
            .Should().OnlyContain(s => s.DrawingNumber == "DR-123");
        var unrelated = Order();
        var delivery = new Delivery("DEL-PRINT", DateTime.UtcNow, order.CustomerId, "operator");
        delivery.AddLine(order.Id, order.ItemId, null, 20m, null, null, null, null);
        delivery.AddLine(unrelated.Id, unrelated.ItemId, null, 10m, null, null, null, null);
        verify.Deliveries.Add(delivery);
        await verify.SaveChangesAsync();
        var loaded = await new GetDeliveryByIdQueryHandler(verify).Handle(new(delivery.Id), default);
        loaded.Lines.Single(l => l.ProductionOrderId == order.Id).DrawingNumbers.Should().Equal("DR-123");
        loaded.Lines.Single(l => l.ProductionOrderId == unrelated.Id).DrawingNumbers.Should().BeEmpty();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetDeliveryByIdQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(loaded);
        var exporter = new Mock<IReportExportService>();
        exporter.Setup(e => e.GeneratePdf(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<object?[]>>()))
            .Returns((string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows) =>
            {
                rows.SelectMany(r => r).Should().Contain("DR-123");
                return new ReportExportService(new TestCompanyIdentityProvider("شركة", "Test Co")).GeneratePdf(title, subtitle, headers, rows);
            });
        // The permit PDF also takes the printable quantity columns; passing none keeps the default
        // (every column), which is what this test is about - the drawing number reaching the paper.
        var pdf = (FileContentResult)await new DeliveriesController(sender.Object).GetDeliveryPdf(delivery.Id, export: exporter.Object);
        System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5).Should().Be("%PDF-");
        pdf.FileContents.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void A_Legacy_Pending_Execution_Cannot_Bypass_The_Drawing_Gate()
    {
        // A persisted legacy pending execution cannot bypass the same domain gate. Phase 4: the legacy
        // Start/Complete/Skip paths were deleted, so the CLOSING path is the only one left to check.
        var stage = (ProductionOrderStageExecution)Activator.CreateInstance(typeof(ProductionOrderStageExecution), nonPublic: true)!;
        typeof(ProductionOrderStageExecution).GetProperty(nameof(stage.RequiresDrawingNumber))!.SetValue(stage, true);
        var transfer = () => stage.CloseWithOutput(95m, null, null, null, null, "operator");
        transfer.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task Printing_Cost_Uses_Posted_Costs_Once_Without_Drawing_Stock_Movements()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var order = Order();
        var stage = order.ActivateNextStage(Printing(), 100m, null, "operator", "COST-DRAW");
        stage.CloseWithOutput(95m, null, null, null, null, "operator");
        db.ProductionOrders.Add(order);
        db.MaterialIssues.Add(new MaterialIssue("CHEM", DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid(), order.Id, 2m, 5m, "operator"));
        db.CostEntries.Add(new CostEntry(order.Id, CostCategory.Labor, 20m, DateTime.UtcNow, "operator", "Printing labor"));
        await db.SaveChangesAsync();
        var cost = await new DyeHouseERP.Application.CostAccounting.Queries.GetProductionOrderCostQueryHandler(db)
            .Handle(new(order.Id), default);
        cost.TotalCost.Should().Be(30m);
        cost.OutputKg.Should().Be(95m);
        cost.OutputMeter.Should().BeNull();
        db.InventoryTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Printing_Service_Price_Does_Not_Replace_Another_Stage_Or_Duplicate_On_Reapply()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var order = Order();
        var printing = Printing();
        var finishing = new ProductionStageDefinition("FIN", "Finishing", 2, "seed");
        db.ProductionOrders.Add(order);
        db.ProductionStageDefinitions.AddRange(printing, finishing);
        await db.SaveChangesAsync();
        var handler = new ApplyServicePriceToJobOrderCommandHandler(db, Mock.Of<ICurrentUserService>(u => u.UserName == "pricing"));
        await handler.Handle(new(order.Id, finishing.Id, UnitOfMeasure.KG, 2m, "approved"), default);
        await handler.Handle(new(order.Id, printing.Id, UnitOfMeasure.KG, 3m, "approved"), default);
        await handler.Handle(new(order.Id, printing.Id, UnitOfMeasure.KG, 4m, "correction"), default);
        await handler.Handle(new(order.Id, printing.Id, UnitOfMeasure.Meter, 5m, "approved"), default);
        (await db.ProductionOrderServicePrices.CountAsync()).Should().Be(3);
        (await db.ProductionOrderServicePrices.SingleAsync(s => s.StageDefinitionId == finishing.Id)).PricePerUnit.Should().Be(2m);
        (await db.ProductionOrderServicePrices.SingleAsync(s => s.StageDefinitionId == printing.Id && s.Unit == UnitOfMeasure.KG)).PricePerUnit.Should().Be(4m);
    }
}
