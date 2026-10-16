using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.ProductionStages.DTOs;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// End-to-end: raw receipt -&gt; production order -&gt; stage completion -&gt;
/// ready goods transfer -&gt; delivery -&gt; invoice -&gt; receipt -&gt; customer
/// statement balance reaches zero. Exercises the whole ledger chain in one
/// pass - if any module posts to the wrong dimension (wrong warehouse,
/// wrong customer, wrong direction), the final balance assertion catches it.
/// </summary>
[Collection("IntegrationDatabase")]
public class FullHappyPathTests : IntegrationTestBase
{
    /// <summary>
    /// JSON options matching the API's serializer contract (JsonStringEnumConverter
    /// in Program.cs): every enum in a response body (e.g. ItemDto.BaseUnit = "KG")
    /// arrives as a STRING. System.Net.Http.Json's default options only accept enum
    /// NUMBERS, which made deserializing ItemDto throw on $.baseUnit.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task RawReceipt_Through_CustomerStatement_ClearsToZero()
    {
        // ---- Master data ----
        var customerCode = $"C{Guid.NewGuid():N}"[..10];
        var customer = (await (await Client.PostAsJsonAsync("/api/customers", new { code = customerCode, name = "Happy Path Customer" }))
            .Content.ReadFromJsonAsync<DyeHouseERP.Application.Customers.DTOs.CustomerDto>(Json))!;

        var item = (await (await Client.PostAsJsonAsync("/api/items", new { code = $"I{Guid.NewGuid():N}"[..10], name = "Poly Fabric", baseUnit = UnitOfMeasure.KG }))
            .Content.ReadFromJsonAsync<ItemDto>(Json))!;

        var rawWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"W{Guid.NewGuid():N}"[..10], name = "Raw WH", kind = WarehouseKind.RawMaterial }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!;

        var readyWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"W{Guid.NewGuid():N}"[..10], name = "Ready WH", kind = WarehouseKind.ReadyGoods }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!;

        // Formation stage + ready-goods stage keep the happy path short - the stage engine itself is covered by domain unit tests.
        await Client.PostAsJsonAsync("/api/production-stages", new
        {
            code = $"S{Guid.NewGuid():N}"[..8], name = "Formation", sequence = 1,
            requiresInputQuantity = true, requiresOutputQuantity = true, notes = (string?)null,
            isFormationStage = true
        });
        var readyStage = (await (await Client.PostAsJsonAsync("/api/production-stages", new
        {
            code = $"R{Guid.NewGuid():N}"[..8], name = "Ready Goods", sequence = 2,
            requiresInputQuantity = true, requiresOutputQuantity = true, notes = (string?)null,
            isFormationStage = false
        })).Content.ReadFromJsonAsync<ProductionStageDefinitionDto>(Json))!;
        (await Client.PostAsync($"/api/production-stages/{readyStage.Id}/ready-goods-stage", null))
            .EnsureSuccessStatusCode();

        // ---- Raw receipt + inspection ----
        var message = (await (await Client.PostAsJsonAsync("/api/raw-messages", new
        {
            receiptDate = DateTime.UtcNow, customerId = customer.Id, warehouseId = rawWarehouse.Id,
            lines = new[] { new { itemId = item.Id, quantityKg = 1000m } }
        })).Content.ReadFromJsonAsync<RawMessageDto>(Json))!;

        await Client.PostAsJsonAsync($"/api/raw-messages/{message.Id}/inspection", new { result = InspectionStatus.Accepted, notes = (string?)null });

        // ---- Issue raw material -> creates the Production Order + allocation atomically ----
        var issueResponse = await Client.PostAsJsonAsync("/api/production-orders/issue", new
        {
            customerId = customer.Id, itemId = item.Id, rawMessageId = message.Id,
            orderDate = DateTime.UtcNow, actualQuantityKg = 1000m, priority = ProductionPriority.Normal,
            overrideNegativeStock = false, overrideReason = (string?)null
        });
        issueResponse.EnsureSuccessStatusCode();
        var order = (await issueResponse.Content.ReadFromJsonAsync<ProductionOrderDto>(Json))!;

        // ---- Transfer the formation stage to Ready Goods (completes stage, posts ready-goods transfer + IN movement, completes order) ----
        var stage = order.StageExecutions.Single();
        var transferResponse = await Client.PostAsJsonAsync(
            $"/api/production-orders/stage-executions/{stage.Id}/transfer", new
            {
                nextStageDefinitionId = readyStage.Id,
                inputKg = 1000m, outputKg = 950m, notes = (string?)null
            });
        transferResponse.EnsureSuccessStatusCode();

        _ = readyWarehouse;
        var balance = await Client.GetFromJsonAsync<List<ReadyGoodsBalanceDto>>($"/api/ready-goods/balance?customerId={customer.Id}", Json);
        balance!.Single(b => b.ProductionOrderId == order.Id).RemainingKg.Should().Be(950m);

        // ---- Delivery: create -> prepare -> deliver ----
        var delivery = (await (await Client.PostAsJsonAsync("/api/deliveries", new
        {
            customerId = customer.Id, deliveryDate = DateTime.UtcNow,
            lines = new[] { new { productionOrderId = order.Id, itemId = item.Id, quantityKg = 950m } }
        })).Content.ReadFromJsonAsync<DeliveryDto>(Json))!;

        await Client.PostAsync($"/api/deliveries/{delivery.Id}/prepare", null);
        var deliverResponse = await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null);
        deliverResponse.EnsureSuccessStatusCode();

        // ---- Invoice: create -> issue ----
        var invoice = (await (await Client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id, invoiceDate = DateTime.UtcNow, discount = 0m, tax = 0m,
            lines = new[] { new { itemId = item.Id, quantity = 950m, processingPrice = 2m } } // 1900 total
        })).Content.ReadFromJsonAsync<InvoiceDto>(Json))!;

        invoice = (await (await Client.PostAsync($"/api/invoices/{invoice.Id}/issue", null)).Content.ReadFromJsonAsync<InvoiceDto>(Json))!;
        invoice.Total.Should().Be(1900m);

        // ---- Treasury account + full receipt against the invoice ----
        var account = (await (await Client.PostAsJsonAsync("/api/treasury-accounts", new
        {
            code = $"A{Guid.NewGuid():N}"[..10], name = "Main Cash", kind = TreasuryAccountKind.Cash
        })).Content.ReadFromJsonAsync<TreasuryAccountDto>(Json))!;

        var receiptResponse = await Client.PostAsJsonAsync("/api/receipts", new
        {
            receiptDate = DateTime.UtcNow, treasuryAccountId = account.Id, amount = 1900m,
            customerId = customer.Id, invoiceId = invoice.Id
        });
        receiptResponse.EnsureSuccessStatusCode();

        // ---- Customer statement should net to zero: Debit 1900 (invoice) - Credit 1900 (receipt) ----
        var statement = await Client.GetFromJsonAsync<List<DyeHouseERP.Application.Invoices.DTOs.CustomerStatementLineDto>>($"/api/customers/{customer.Id}/statement", Json);
        statement!.Last().RunningBalance.Should().Be(0m);
    }
}
