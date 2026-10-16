using System.Net;
using System.Net.Http.Json;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

[Collection("IntegrationDatabase")]
public class PrintingSqlTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Api_Rejects_Missing_Drawing_Then_Persists_Valid_Drawing_On_The_Correct_Order()
    {
        var (customer, item, raw, _) = await SeedMasterDataAsync("PRINT");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProductionStageDefinitions.Add(new ProductionStageDefinition("FORMATION", "Formation", 1, "seed", isFormationStage: true));
            db.ProductionStageDefinitions.Add(new ProductionStageDefinition("PRINTING", "Printing", 30, "seed", requiresDrawingNumber: true));
            await db.SaveChangesAsync();
        }
        var message = await ReceiveAndAcceptAsync(customer, item, raw, 100m);
        var orderId = await IssueOrderAsync(customer, item, message, 100m);
        var order = (await Client.GetFromJsonAsync<ProductionOrderDto>($"/api/production-orders/{orderId}", Json))!;
        var stage = order.StageExecutions.Single();
        Guid printingId;
        using (var scope = Factory.Services.CreateScope())
            printingId = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProductionStageDefinitions.Single(s => s.Code == "PRINTING").Id;
        var rejected = await Client.PostAsJsonAsync($"/api/production-orders/stage-executions/{stage.Id}/transfer", new { nextStageDefinitionId = printingId, outputKg = 95m });
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var accepted = await Client.PostAsJsonAsync($"/api/production-orders/stage-executions/{stage.Id}/transfer", new { nextStageDefinitionId = printingId, outputKg = 95m, drawingNumber = "SQL-DR-1" });
        accepted.EnsureSuccessStatusCode();
        var refreshed = (await Client.GetFromJsonAsync<ProductionOrderDto>($"/api/production-orders/{orderId}", Json))!;
        refreshed.StageExecutions.Single(s => s.StageDefinitionId == printingId).DrawingNumber.Should().Be("SQL-DR-1");
    }
}
