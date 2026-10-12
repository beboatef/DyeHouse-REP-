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
    public void Drawing_Is_Trimmed_And_Closed_Stage_Cannot_Be_Corrected_Or_Skipped()
    {
        var stage = Order().ActivateNextStage(Printing(), 100m, null, "operator", " DR-123 ");
        stage.DrawingNumber.Should().Be("DR-123");
        stage.CloseWithOutput(95m, null, null, null, null, "operator");
        var edit = () => stage.SetDrawingNumber("OTHER");
        var skip = () => stage.Skip(true, "bypass", "operator");
        edit.Should().Throw<DomainException>();
        skip.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task Drawing_Persists_Across_Transfers_And_Partial_Deliveries_And_Is_Passed_To_Pdf()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var order = Order();
        var printing = Printing();
        var finishing = new ProductionStageDefinition("FINISH", "Finishing", 2, "seed");
        var stage = order.ActivateNextStage(printing, 100m, null, "operator", "DR-123");
        var user = Mock.Of<ICurrentUserService>(u => u.UserName == "operator");
        await using (var db = new ApplicationDbContext(options))
        {
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
                return new ReportExportService().GeneratePdf(title, subtitle, headers, rows);
            });
        var pdf = (FileContentResult)await new DeliveriesController(sender.Object).GetDeliveryPdf(delivery.Id, exporter.Object);
        System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5).Should().Be("%PDF-");
        pdf.FileContents.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Legacy_Completion_And_Start_Cannot_Bypass_Drawing_Gate()
    {
        // A persisted legacy pending execution cannot bypass the same domain gate.
        var stage = (ProductionOrderStageExecution)Activator.CreateInstance(typeof(ProductionOrderStageExecution), nonPublic: true)!;
        typeof(ProductionOrderStageExecution).GetProperty(nameof(stage.RequiresDrawingNumber))!.SetValue(stage, true);
        var start = () => stage.Start("operator");
        var complete = () => stage.Complete(100m, null, 95m, null, 5m, null, null, null, null, false, null);
        var transfer = () => stage.CloseWithOutput(95m, null, null, null, null, "operator");
        start.Should().Throw<DomainException>();
        complete.Should().Throw<DomainException>();
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
