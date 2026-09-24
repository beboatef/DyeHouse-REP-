using System.Net.Http.Json;
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
public class FullHappyPathTests : IntegrationTestBase
{
    [Fact]
    public async Task RawReceipt_Through_CustomerStatement_ClearsToZero()
    {
        // ---- Master data ----
        var customerCode = $"C{Guid.NewGuid():N}"[..10];
        var customer = (await (await Client.PostAsJsonAsync("/api/customers", new { code = customerCode, name = "Happy Path Customer" }))
            .Content.ReadFromJsonAsync<DyeHouseERP.Application.Customers.DTOs.CustomerDto>())!;

        var item = (await (await Client.PostAsJsonAsync("/api/items", new { code = $"I{Guid.NewGuid():N}"[..10], name = "Poly Fabric", baseUnit = UnitOfMeasure.KG }))
            .Content.ReadFromJsonAsync<ItemDto>())!;

        var rawWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"W{Guid.NewGuid():N}"[..10], name = "Raw WH", kind = WarehouseKind.RawMaterial }))
            .Content.ReadFromJsonAsync<WarehouseDto>())!;

        var readyWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"W{Guid.NewGuid():N}"[..10], name = "Ready WH", kind = WarehouseKind.ReadyGoods }))
            .Content.ReadFromJsonAsync<WarehouseDto>())!;

        // One active stage keeps the happy path short - the stage engine itself is covered by domain unit tests.
        await Client.PostAsJsonAsync("/api/production-stages", new
        {
            code = $"S{Guid.NewGuid():N}"[..8], name = "Dyeing", sequence = 1,
            requiresInputQuantity = true, requiresOutputQuantity = true, requiresApproval = false,
            allowSkip = false, allowRepeat = false, allowRework = true, allowReturn = false, notes = (string?)null
        });

        // ---- Raw receipt + inspection ----
        var message = (await (await Client.PostAsJsonAsync("/api/raw-messages", new
        {
            receiptDate = DateTime.UtcNow, customerId = customer.Id, warehouseId = rawWarehouse.Id,
            lines = new[] { new { itemId = item.Id, quantityKg = 1000m } }
        })).Content.ReadFromJsonAsync<RawMessageDto>())!;

        await Client.PostAsJsonAsync($"/api/raw-messages/{message.Id}/inspection", new { result = InspectionStatus.Accepted, notes = (string?)null });

        // ---- Production order + raw allocation ----
        var order = (await (await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId = customer.Id, itemId = item.Id, orderDate = DateTime.UtcNow,
            requestedQuantityKg = 1000m, priority = ProductionPriority.Normal
        })).Content.ReadFromJsonAsync<ProductionOrderDto>())!;

        order = (await (await Client.PostAsJsonAsync($"/api/production-orders/{order.Id}/raw-allocations", new
        {
            rawMessageId = message.Id, quantityKg = 1000m, overrideNegativeStock = false, overrideReason = (string?)null
        })).Content.ReadFromJsonAsync<ProductionOrderDto>())!;

        // ---- Complete the single stage ----
        var stage = order.StageExecutions.Single();
        await Client.PostAsync($"/api/production-orders/stage-executions/{stage.Id}/start", null);
        var completeResponse = await Client.PostAsJsonAsync($"/api/production-orders/stage-executions/{stage.Id}/complete", new
        {
            inputKg = 1000m, outputKg = 950m, lossKg = 50m, notes = (string?)null, approvedBy = (string?)null
        });
        completeResponse.EnsureSuccessStatusCode();

        await Client.PostAsync($"/api/production-orders/{order.Id}/complete", null);

        // ---- Ready goods transfer ----
        var readyTransferResponse = await Client.PostAsJsonAsync("/api/ready-goods/transfers", new
        {
            productionOrderId = order.Id, warehouseId = readyWarehouse.Id, quantityKg = 950m
        });
        readyTransferResponse.EnsureSuccessStatusCode();

        var balance = await Client.GetFromJsonAsync<List<ReadyGoodsBalanceDto>>($"/api/ready-goods/balance?customerId={customer.Id}");
        balance!.Single(b => b.ProductionOrderId == order.Id).RemainingKg.Should().Be(950m);

        // ---- Delivery: create -> prepare -> deliver ----
        var delivery = (await (await Client.PostAsJsonAsync("/api/deliveries", new
        {
            customerId = customer.Id, deliveryDate = DateTime.UtcNow,
            lines = new[] { new { productionOrderId = order.Id, itemId = item.Id, quantityKg = 950m } }
        })).Content.ReadFromJsonAsync<DeliveryDto>())!;

        await Client.PostAsync($"/api/deliveries/{delivery.Id}/prepare", null);
        var deliverResponse = await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null);
        deliverResponse.EnsureSuccessStatusCode();

        // ---- Invoice: create -> issue ----
        var invoice = (await (await Client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id, invoiceDate = DateTime.UtcNow, discount = 0m, tax = 0m,
            lines = new[] { new { itemId = item.Id, quantity = 950m, processingPrice = 2m } } // 1900 total
        })).Content.ReadFromJsonAsync<InvoiceDto>())!;

        invoice = (await (await Client.PostAsync($"/api/invoices/{invoice.Id}/issue", null)).Content.ReadFromJsonAsync<InvoiceDto>())!;
        invoice.Total.Should().Be(1900m);

        // ---- Treasury account + full receipt against the invoice ----
        var account = (await (await Client.PostAsJsonAsync("/api/treasury-accounts", new
        {
            code = $"A{Guid.NewGuid():N}"[..10], name = "Main Cash", kind = TreasuryAccountKind.Cash
        })).Content.ReadFromJsonAsync<TreasuryAccountDto>())!;

        var receiptResponse = await Client.PostAsJsonAsync("/api/receipts", new
        {
            receiptDate = DateTime.UtcNow, treasuryAccountId = account.Id, amount = 1900m,
            customerId = customer.Id, invoiceId = invoice.Id
        });
        receiptResponse.EnsureSuccessStatusCode();

        // ---- Customer statement should net to zero: Debit 1900 (invoice) - Credit 1900 (receipt) ----
        var statement = await Client.GetFromJsonAsync<List<DyeHouseERP.Application.Invoices.DTOs.CustomerStatementLineDto>>($"/api/customers/{customer.Id}/statement");
        statement!.Last().RunningBalance.Should().Be(0m);
    }
}
