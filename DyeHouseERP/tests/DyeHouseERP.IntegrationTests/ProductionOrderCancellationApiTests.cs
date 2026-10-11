using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// S24 API coverage: production order cancellation is reachable through the
/// HTTP layer with the right status codes, restores the raw balance through the
/// ledger (never edits it), is refused on a repeat, and can never touch a
/// completed order.
///
/// These tests require the real SQL Server integration setup (see
/// IntegrationTestBase) - they build with the solution but are only executed
/// where a server is reachable.
/// </summary>
[Collection("IntegrationDatabase")]
public class ProductionOrderCancellationApiTests : IntegrationTestBase
{
    /// <summary>API serializer contract: enums arrive as strings (JsonStringEnumConverter in Program.cs).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<(Guid CustomerId, Guid ItemId, Guid WarehouseId)> SeedMasterDataAsync()
    {
        var customerCode = $"C{Guid.NewGuid():N}"[..10];
        var customerResponse = await Client.PostAsJsonAsync("/api/customers", new { code = customerCode, name = "Cancel Customer" });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<DyeHouseERP.Application.Customers.DTOs.CustomerDto>(Json);

        var itemCode = $"I{Guid.NewGuid():N}"[..10];
        var itemResponse = await Client.PostAsJsonAsync("/api/items", new { code = itemCode, name = "Poly Fabric", baseUnit = UnitOfMeasure.KG });
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDto>(Json);

        var warehouseCode = $"W{Guid.NewGuid():N}"[..10];
        var warehouseResponse = await Client.PostAsJsonAsync("/api/warehouses", new { code = warehouseCode, name = "Cancel Raw WH", kind = WarehouseKind.RawMaterial });
        var warehouse = await warehouseResponse.Content.ReadFromJsonAsync<WarehouseDto>(Json);

        return (customer!.Id, item!.Id, warehouse!.Id);
    }

    private async Task<RawMessageDto> ReceiveAndAcceptRawMessageAsync(Guid customerId, Guid itemId, Guid warehouseId, decimal quantityKg)
    {
        var createResponse = await Client.PostAsJsonAsync("/api/raw-messages", new
        {
            receiptDate = DateTime.UtcNow,
            customerId,
            warehouseId,
            lines = new[] { new { itemId, quantityKg } }
        });
        createResponse.EnsureSuccessStatusCode();
        var message = (await createResponse.Content.ReadFromJsonAsync<RawMessageDto>(Json))!;

        var inspectionResponse = await Client.PostAsJsonAsync($"/api/raw-messages/{message.Id}/inspection",
            new { result = InspectionStatus.Accepted, notes = (string?)null });
        inspectionResponse.EnsureSuccessStatusCode();

        return message;
    }

    private async Task<ProductionOrderDto> CreateOrderAsync(Guid customerId, Guid itemId, decimal quantityKg)
    {
        var response = await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId, itemId, orderDate = DateTime.UtcNow, requestedQuantityKg = quantityKg,
            priority = ProductionPriority.Normal
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductionOrderDto>(Json))!;
    }

    private async Task<ProductionOrderDto> AllocateAsync(Guid orderId, Guid rawMessageId, Guid itemId, decimal quantityKg)
    {
        var response = await Client.PostAsJsonAsync($"/api/production-orders/{orderId}/raw-allocations", new
        {
            rawMessageId, itemId, quantityKg, overrideNegativeStock = false, overrideReason = (string?)null
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductionOrderDto>(Json))!;
    }

    /// <summary>The message's live remaining balance, read back from the raw-message screen's own query.</summary>
    private async Task<decimal?> RemainingKgAsync(Guid customerId, Guid messageId)
    {
        var response = await Client.GetAsync($"/api/raw-messages?customerId={customerId}");
        response.EnsureSuccessStatusCode();
        var messages = (await response.Content.ReadFromJsonAsync<List<RawMessageDto>>(Json))!;
        return messages.Single(m => m.Id == messageId).Lines.Single().RemainingKg;
    }

    [Fact]
    public async Task Cancel_AnAllocatedOrder_ReturnsOk_MarksItCancelled_AndRestoresTheRawBalance()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 1000m);
        var order = await CreateOrderAsync(customerId, itemId, quantityKg: 600m);
        await AllocateAsync(order.Id, message.Id, itemId, quantityKg: 600m);

        (await RemainingKgAsync(customerId, message.Id)).Should().Be(400m, "600 was issued to the job");

        var cancelResponse = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/cancel",
            new { reason = "customer withdrew the job" });

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<ProductionOrderDto>(Json);
        cancelled!.Status.Should().Be(ProductionOrderStatus.Cancelled);
        cancelled.OrderNumber.Should().Be(order.OrderNumber);

        // The ledger restored the balance - no balance column was ever edited.
        (await RemainingKgAsync(customerId, message.Id)).Should().Be(1000m);
    }

    [Fact]
    public async Task Cancel_Repeated_Returns422_AndDoesNotDoubleTheReversal()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 1000m);
        var order = await CreateOrderAsync(customerId, itemId, quantityKg: 600m);
        await AllocateAsync(order.Id, message.Id, itemId, quantityKg: 600m);

        var first = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/cancel",
            new { reason = "first" });
        first.EnsureSuccessStatusCode();

        var second = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/cancel",
            new { reason = "again" });

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a repeat cancellation is a business-rule refusal, not a server fault");

        // Exactly one reversal: the balance is the received quantity, not MORE.
        (await RemainingKgAsync(customerId, message.Id)).Should().Be(1000m);
    }

    [Fact]
    public async Task Cancel_WithoutAReason_Returns400()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 500m);
        var order = await CreateOrderAsync(customerId, itemId, quantityKg: 200m);
        await AllocateAsync(order.Id, message.Id, itemId, quantityKg: 200m);

        var response = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/cancel",
            new { reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RemainingKgAsync(customerId, message.Id)).Should().Be(300m, "nothing may be posted for an invalid request");
    }

    [Fact]
    public async Task Cancel_ACompletedOrder_Returns409()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 1000m);
        var order = await CreateOrderAsync(customerId, itemId, quantityKg: 600m);
        order = await AllocateAsync(order.Id, message.Id, itemId, quantityKg: 600m);

        // Drive the order to Completed. Spec sections 22-23: an order that
        // produced anything is completed by transferring its stage to the
        // ready-goods stage - that one step closes the stage, posts the output
        // to Ready Goods (linked to that stage execution) and completes the
        // order. POST /complete is refused for positive output by design.
        var stage = order.StageExecutions.Single();
        var stageDefinitions = await Client.GetFromJsonAsync<List<ProductionStageDefinitionDto>>(
            "/api/production-stages", Json);
        var readyGoodsStage = stageDefinitions!.Single(s => s.IsReadyGoodsStage);

        var transfer = await Client.PostAsJsonAsync(
            $"/api/production-orders/stage-executions/{stage.Id}/transfer", new
        {
            nextStageDefinitionId = readyGoodsStage.Id,
            outputKg = 580m, outputMeter = (decimal?)null,
            separatesKg = (decimal?)null, separatesMeter = (decimal?)null,
            notes = (string?)null
        });
        transfer.EnsureSuccessStatusCode();

        var cancelResponse = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/cancel",
            new { reason = "too late" });

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a completed order is finalized - cancellation is refused before any ledger work");

        (await RemainingKgAsync(customerId, message.Id)).Should().Be(400m,
            "the issue stays exactly as posted - a refused cancellation posts nothing");
    }
}
