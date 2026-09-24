using System.Net;
using System.Net.Http.Json;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Exercises spec sections 4/13 (manual, no-FIFO raw allocation) and 17
/// (negative stock is blocked by default, and only proceeds with an
/// explicit override + permission).
/// </summary>
public class RawAllocationAndNegativeStockTests : IntegrationTestBase
{
    private async Task<(Guid CustomerId, Guid ItemId, Guid WarehouseId)> SeedMasterDataAsync()
    {
        var customerCode = $"C{Guid.NewGuid():N}"[..10];
        var customerResponse = await Client.PostAsJsonAsync("/api/customers", new { code = customerCode, name = "Test Customer" });
        var customer = await customerResponse.Content.ReadFromJsonAsync<DyeHouseERP.Application.Customers.DTOs.CustomerDto>();

        var itemCode = $"I{Guid.NewGuid():N}"[..10];
        var itemResponse = await Client.PostAsJsonAsync("/api/items", new { code = itemCode, name = "Cotton Fabric", baseUnit = UnitOfMeasure.KG });
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemDto>();

        var warehouseCode = $"W{Guid.NewGuid():N}"[..10];
        var warehouseResponse = await Client.PostAsJsonAsync("/api/warehouses", new { code = warehouseCode, name = "Main Raw Warehouse", kind = WarehouseKind.RawMaterial });
        var warehouse = await warehouseResponse.Content.ReadFromJsonAsync<WarehouseDto>();

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
        var message = (await createResponse.Content.ReadFromJsonAsync<RawMessageDto>())!;

        var inspectionResponse = await Client.PostAsJsonAsync($"/api/raw-messages/{message.Id}/inspection",
            new { result = InspectionStatus.Accepted, notes = (string?)null });
        inspectionResponse.EnsureSuccessStatusCode();

        return message;
    }

    [Fact]
    public async Task AllocateRaw_WithinBalance_Succeeds_AndReflectsInRemainingBalance()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 1000m);

        var orderResponse = await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId, itemId, orderDate = DateTime.UtcNow, requestedQuantityKg = 600m, priority = ProductionPriority.Normal
        });
        orderResponse.EnsureSuccessStatusCode();
        var order = (await orderResponse.Content.ReadFromJsonAsync<ProductionOrderDto>())!;

        var allocateResponse = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/raw-allocations", new
        {
            rawMessageId = message.Id, quantityKg = 600m, overrideNegativeStock = false, overrideReason = (string?)null
        });

        allocateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedOrder = await allocateResponse.Content.ReadFromJsonAsync<ProductionOrderDto>();
        updatedOrder!.RawAllocations.Should().ContainSingle(a => a.RawMessageId == message.Id && a.QuantityKg == 600m);
        updatedOrder.Status.Should().Be(ProductionOrderStatus.RawAllocated);

        // The message's remaining balance should now reflect the 600 KG drawn from 1000 KG received.
        var messagesResponse = await Client.GetAsync($"/api/raw-messages?customerId={customerId}");
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<RawMessageDto>>();
        messages!.Single(m => m.Id == message.Id).Lines.Single().RemainingKg.Should().Be(400m);
    }

    [Fact]
    public async Task AllocateRaw_BeyondBalance_WithoutOverride_Returns409NegativeStock()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();
        var message = await ReceiveAndAcceptRawMessageAsync(customerId, itemId, warehouseId, quantityKg: 500m);

        var orderResponse = await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId, itemId, orderDate = DateTime.UtcNow, requestedQuantityKg = 550m, priority = ProductionPriority.Normal
        });
        var order = (await orderResponse.Content.ReadFromJsonAsync<ProductionOrderDto>())!;

        // Requesting 550 KG from a message that only received 500 KG - spec section 17 example exactly.
        var allocateResponse = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/raw-allocations", new
        {
            rawMessageId = message.Id, quantityKg = 550m, overrideNegativeStock = false, overrideReason = (string?)null
        });

        allocateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await allocateResponse.Content.ReadAsStringAsync();
        body.Should().Contain("NEGATIVE_STOCK");
    }

    [Fact]
    public async Task AllocateRaw_FromUninspectedMessage_IsRejected()
    {
        var (customerId, itemId, warehouseId) = await SeedMasterDataAsync();

        var createResponse = await Client.PostAsJsonAsync("/api/raw-messages", new
        {
            receiptDate = DateTime.UtcNow, customerId, warehouseId,
            lines = new[] { new { itemId, quantityKg = 300m } }
        });
        var message = (await createResponse.Content.ReadFromJsonAsync<RawMessageDto>())!;
        // Deliberately NOT calling the inspection endpoint - message stays PendingInspection.

        var orderResponse = await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId, itemId, orderDate = DateTime.UtcNow, requestedQuantityKg = 100m, priority = ProductionPriority.Normal
        });
        var order = (await orderResponse.Content.ReadFromJsonAsync<ProductionOrderDto>())!;

        var allocateResponse = await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/raw-allocations", new
        {
            rawMessageId = message.Id, quantityKg = 100m, overrideNegativeStock = false, overrideReason = (string?)null
        });

        // DomainException path (not NegativeStock) -> mapped to 422 by ExceptionHandlingMiddleware.
        allocateResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
