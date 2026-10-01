using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Invoices.DTOs;
using DyeHouseERP.Application.Items.DTOs;
using DyeHouseERP.Application.ProductionOrders.DTOs;
using DyeHouseERP.Application.RawReceipts.DTOs;
using DyeHouseERP.Application.ReadyGoods.DTOs;
using DyeHouseERP.Application.Treasury.DTOs;
using DyeHouseERP.Application.Users.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Shared seeding for the concurrency suite (Category=Concurrency). Every test
/// seeds its OWN master data with unique codes, so tests never share state
/// even when they share a database. Tests run against the existing local SQL
/// Server integration setup (CustomWebApplicationFactory: DYEHOUSE_TEST_CONNECTION
/// or the localhost:1433 default) - no Docker and no Testcontainers involved.
/// A real SQL Server is REQUIRED: sp_getapplock and rowversion have no
/// InMemory equivalent, so when no server is reachable the tests fail with
/// the real connection error - they are never silently skipped or faked.
/// </summary>
public abstract class SqlServerConcurrencyTestBase : IntegrationTestBase
{
    private static int _stageSequence = 1;

    /// <summary>
    /// JSON options matching the API's serializer contract. The API registers
    /// JsonStringEnumConverter in Program.cs, so every enum in a response body
    /// (e.g. ItemDto.BaseUnit = "KG") is a STRING; System.Net.Http.Json's
    /// default options only accept enum NUMBERS, which is why deserializing
    /// ItemDto threw "The JSON value could not be converted to UnitOfMeasure"
    /// on $.baseUnit. Posting still works without these options (the server's
    /// converter accepts numbers too), but every response read must use them.
    /// </summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Barrier point for concurrent workers: seeding is already complete when
    /// InitializeAsync returns (tests run sequentially inside a class), so
    /// this is intentionally trivial - the race comes from the workers firing
    /// HTTP requests at each other.
    /// </summary>
    protected Task ReadyAsync => Task.CompletedTask;

    // ---------------------------------------------------------------------
    // Seeding helpers - unique codes per call, isolated data per test.
    // ---------------------------------------------------------------------
    protected async Task<(Guid CustomerId, Guid ItemId, Guid RawWarehouseId, Guid ReadyWarehouseId)>
        SeedMasterDataAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // One active production stage so completed orders can transfer to ready goods.
        await Client.PostAsJsonAsync("/api/production-stages", new
        {
            code = $"S{Guid.NewGuid():N}"[..8], name = "Dyeing", sequence = Interlocked.Increment(ref _stageSequence),
            requiresInputQuantity = true, requiresOutputQuantity = true, requiresApproval = false,
            allowSkip = false, allowRepeat = false, allowRework = true, allowReturn = false, notes = (string?)null
        });

        var customer = (await (await Client.PostAsJsonAsync("/api/customers", new { code = $"{prefix}C{suffix}", name = "Conc Customer" }))
            .Content.ReadFromJsonAsync<CustomerDto>(Json))!;
        var item = (await (await Client.PostAsJsonAsync("/api/items", new { code = $"{prefix}I{suffix}", name = "Conc Fabric", baseUnit = UnitOfMeasure.KG }))
            .Content.ReadFromJsonAsync<ItemDto>(Json))!;
        var rawWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"{prefix}W{suffix}", name = "Raw WH", kind = WarehouseKind.RawMaterial }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!;
        var readyWarehouse = (await (await Client.PostAsJsonAsync("/api/warehouses", new { code = $"{prefix}R{suffix}", name = "Ready WH", kind = WarehouseKind.ReadyGoods }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!;
        return (customer.Id, item.Id, rawWarehouse.Id, readyWarehouse.Id);
    }

    protected async Task<Guid> ReceiveAndAcceptAsync(Guid customerId, Guid itemId, Guid warehouseId, decimal kg)
    {
        var message = (await (await Client.PostAsJsonAsync("/api/raw-messages", new
        {
            receiptDate = DateTime.UtcNow, customerId, warehouseId,
            lines = new[] { new { itemId, quantityKg = kg } }
        })).Content.ReadFromJsonAsync<RawMessageDto>(Json))!;
        (await Client.PostAsJsonAsync($"/api/raw-messages/{message.Id}/inspection", new { result = InspectionStatus.Accepted, notes = (string?)null }))
            .EnsureSuccessStatusCode();
        return message.Id;
    }

    protected async Task<Guid> CreateOrderAsync(Guid customerId, Guid itemId, decimal requestedKg)
    {
        var order = (await (await Client.PostAsJsonAsync("/api/production-orders", new
        {
            customerId, itemId, orderDate = DateTime.UtcNow,
            requestedQuantityKg = requestedKg, priority = ProductionPriority.Normal
        })).Content.ReadFromJsonAsync<ProductionOrderDto>(Json))!;
        return order.Id;
    }

    protected async Task AllocateAsync(Guid orderId, Guid itemId, Guid messageId, decimal kg)
    {
        (await Client.PostAsJsonAsync($"/api/production-orders/{orderId}/raw-allocations", new
        { rawMessageId = messageId, itemId, quantityKg = kg, overrideNegativeStock = false, overrideReason = (string?)null }))
            .EnsureSuccessStatusCode();
    }

    /// <summary>Completes all stages and posts the ready-goods transfer; returns (orderId, transferId).</summary>
    protected async Task<(Guid OrderId, Guid TransferId)> ProduceAndTransferAsync(Guid orderId, Guid readyWarehouseId, decimal inputKg, decimal outputKg)
    {
        var order = await Client.GetFromJsonAsync<ProductionOrderDto>($"/api/production-orders/{orderId}", Json);
        foreach (var stage in order!.StageExecutions)
        {
            await Client.PostAsync($"/api/production-orders/stage-executions/{stage.Id}/start", null);
            (await Client.PostAsJsonAsync($"/api/production-orders/stage-executions/{stage.Id}/complete", new
            { inputKg, outputKg, lossKg = inputKg - outputKg, notes = (string?)null, approvedBy = (string?)null })).EnsureSuccessStatusCode();
        }
        (await Client.PostAsync($"/api/production-orders/{orderId}/complete", null)).EnsureSuccessStatusCode();

        var transferResponse = await Client.PostAsJsonAsync("/api/ready-goods/transfers", new
        { productionOrderId = orderId, warehouseId = readyWarehouseId, quantityKg = outputKg });
        transferResponse.EnsureSuccessStatusCode();
        var transfer = await transferResponse.Content.ReadFromJsonAsync<ReadyGoodsTransferDto>(Json);
        return (orderId, transfer!.Id);
    }

    protected async Task<DeliveryDto> CreatePreparedDeliveryAsync(Guid customerId, Guid orderId, Guid itemId, decimal kg)
    {
        var delivery = (await (await Client.PostAsJsonAsync("/api/deliveries", new
        {
            customerId, deliveryDate = DateTime.UtcNow,
            lines = new[] { new { productionOrderId = orderId, itemId, quantityKg = kg } }
        })).Content.ReadFromJsonAsync<DeliveryDto>(Json))!;
        (await Client.PostAsync($"/api/deliveries/{delivery.Id}/prepare", null)).EnsureSuccessStatusCode();
        return delivery;
    }

    protected async Task<decimal> ReadyBalanceAsync(Guid customerId, Guid orderId)
    {
        var balance = await Client.GetFromJsonAsync<List<ReadyGoodsBalanceDto>>($"/api/ready-goods/balance?customerId={customerId}", Json);
        return balance!.FirstOrDefault(b => b.ProductionOrderId == orderId)?.RemainingKg ?? 0m;
    }

    protected static decimal BalanceKgOf(List<RawMessageDto> messages, Guid messageId) =>
        messages.Single(m => m.Id == messageId).Lines.Single().RemainingKg ?? 0m;
}

/// <summary>T1: allocation vs external-processing release race on the same raw lot, stock covers only one.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class AllocateVersusExternalReleaseRaceTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Only_One_Racing_Operation_Wins_And_Balance_Never_Goes_Negative()
    {
        var (customerId, itemId, rawWarehouseId, _) = await SeedMasterDataAsync("t1");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 1000m);

        // Order backing the external-processing stage movement (no allocation yet).
        var epOrderId = await CreateOrderAsync(customerId, itemId, requestedKg: 600m);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var allocateTask = Task.Run(async () =>
        {
            await ReadyAsync;
            var order2 = await CreateOrderAsync(customerId, itemId, requestedKg: 600m);
            var response = await Client.PostAsJsonAsync($"/api/production-orders/{order2}/raw-allocations", new
            { rawMessageId = messageId, itemId, quantityKg = 600m, overrideNegativeStock = false, overrideReason = (string?)null });
            return response.StatusCode;
        });

        var releaseTask = Task.Run(async () =>
        {
            await ReadyAsync;
            var response = await Client.PostAsJsonAsync("/api/raw-external-releases", new
            {
                customerId, itemId, rawMessageId = messageId, quantityKg = 600m,
                reason = RawReleaseReason.ExternalProcessing, externalParty = "Processor Co",
                overrideNegativeStock = false, overrideReason = (string?)null,
                productionOrderId = epOrderId, externalProcessingStage = "Dyeing",
                externalProcessingCost = (decimal?)50m, expectedReturnDate = (DateTime?)null
            });
            return response.StatusCode;
        });

        start.SetResult();
        var statuses = await Task.WhenAll(allocateTask, releaseTask);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(1, "stock covers only one 600 KG operation");
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(1, "the loser fails with NEGATIVE_STOCK (409)");

        var messages = await Client.GetFromJsonAsync<List<RawMessageDto>>($"/api/raw-messages?customerId={customerId}", Json);
        BalanceKgOf(messages!, messageId).Should().Be(400m, "balance lands exactly at 1000 - 600, never negative");
    }
}

/// <summary>T2: ten parallel allocations over stock covering only three.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class TenParallelAllocationTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Exactly_Three_Of_Ten_Parallel_Allocations_Succeed()
    {
        var (customerId, itemId, rawWarehouseId, _) = await SeedMasterDataAsync("t2");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 300m);

        var orders = new List<Guid>();
        for (var i = 0; i < 10; i++)
            orders.Add(await CreateOrderAsync(customerId, itemId, requestedKg: 100m));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = orders.Select(o => Task.Run(async () =>
        {
            await ReadyAsync;
            var response = await Client.PostAsJsonAsync($"/api/production-orders/{o}/raw-allocations", new
            { rawMessageId = messageId, itemId, quantityKg = 100m, overrideNegativeStock = false, overrideReason = (string?)null });
            return response.StatusCode;
        })).ToList();

        start.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(3, "stock of 300 covers exactly three 100 KG allocations");
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(7, "the rest fail with NEGATIVE_STOCK");

        var messages = await Client.GetFromJsonAsync<List<RawMessageDto>>($"/api/raw-messages?customerId={customerId}", Json);
        BalanceKgOf(messages!, messageId).Should().Be(0m, "all stock is consumed exactly once");
    }
}

/// <summary>T3: two deliveries race for the same order's ready stock.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class TwoDeliveriesRaceTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Total_Delivered_Never_Exceeds_Ready_Balance()
    {
        var (customerId, itemId, rawWarehouseId, readyWarehouseId) = await SeedMasterDataAsync("t3");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 500m);
        var orderId = await CreateOrderAsync(customerId, itemId, 500m);
        await AllocateAsync(orderId, itemId, messageId, 500m);
        await ProduceAndTransferAsync(orderId, readyWarehouseId, inputKg: 500m, outputKg: 400m);

        var d1 = await CreatePreparedDeliveryAsync(customerId, orderId, itemId, kg: 400m);
        var d2 = await CreatePreparedDeliveryAsync(customerId, orderId, itemId, kg: 400m);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { d1.Id, d2.Id }.Select(id => Task.Run(async () =>
        {
            await ReadyAsync;
            return (await Client.PostAsync($"/api/deliveries/{id}/deliver", null)).StatusCode;
        })).ToList();

        start.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(1, "only one 400 KG delivery fits the 400 KG lot");
        statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(1, "the loser fails with NEGATIVE_STOCK");
        (await ReadyBalanceAsync(customerId, orderId)).Should().Be(0m, "never negative");
    }
}

/// <summary>T4: one delivery whose duplicate lines jointly overdraw the lot.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class DuplicateLineDeliveryTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Delivery_With_Duplicate_Lines_Overdrawing_The_Lot_Is_Rejected()
    {
        var (customerId, itemId, rawWarehouseId, readyWarehouseId) = await SeedMasterDataAsync("t4");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 500m);
        var orderId = await CreateOrderAsync(customerId, itemId, 500m);
        await AllocateAsync(orderId, itemId, messageId, 500m);
        await ProduceAndTransferAsync(orderId, readyWarehouseId, inputKg: 500m, outputKg: 400m);

        // Same (order, item) twice: 300 + 300 = 600 > 400 available.
        var delivery = (await (await Client.PostAsJsonAsync("/api/deliveries", new
        {
            customerId, deliveryDate = DateTime.UtcNow,
            lines = new object[]
            {
                new { productionOrderId = orderId, itemId, quantityKg = 300m },
                new { productionOrderId = orderId, itemId, quantityKg = 300m }
            }
        })).Content.ReadFromJsonAsync<DeliveryDto>(Json))!;
        (await Client.PostAsync($"/api/deliveries/{delivery.Id}/prepare", null)).EnsureSuccessStatusCode();

        var response = await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the combined duplicate quantity exceeds the lot");
        (await ReadyBalanceAsync(customerId, orderId)).Should().Be(400m, "nothing was deducted");
    }
}

/// <summary>T5: two concurrent cancellations of the same transfer create exactly one reversal set.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class DoubleCancellationTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Two_Concurrent_Cancels_Produce_Exactly_One_Reversal_Set()
    {
        var (customerId, itemId, rawWarehouseId, readyWarehouseId) = await SeedMasterDataAsync("t5");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 500m);
        var orderId = await CreateOrderAsync(customerId, itemId, 500m);
        await AllocateAsync(orderId, itemId, messageId, 500m);
        var (_, transferId) = await ProduceAndTransferAsync(orderId, readyWarehouseId, inputKg: 500m, outputKg: 500m);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await ReadyAsync;
            var response = await Client.DeleteAsync($"/api/ready-goods/transfers/{transferId}?reason=double-cancel-race");
            return response.StatusCode;
        })).ToList();

        start.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == HttpStatusCode.NoContent).Should().Be(1, "only one cancellation may win");
        statuses.Count(s => s == HttpStatusCode.UnprocessableEntity).Should().Be(1, "the loser gets the already-cancelled 422");

        (await ReadyBalanceAsync(customerId, orderId)).Should().Be(0m, "reversed exactly once: 500 IN minus one 500 reversal");
    }
}

/// <summary>T6: delivery races transfer cancellation; ready balance must never go negative.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class DeliveryVersusCancellationTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Delivery_And_Cancellation_Serialize_And_Balance_Stays_Nonnegative()
    {
        var (customerId, itemId, rawWarehouseId, readyWarehouseId) = await SeedMasterDataAsync("t6");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 500m);
        var orderId = await CreateOrderAsync(customerId, itemId, 500m);
        await AllocateAsync(orderId, itemId, messageId, 500m);
        var (_, transferId) = await ProduceAndTransferAsync(orderId, readyWarehouseId, inputKg: 500m, outputKg: 500m);

        var delivery = await CreatePreparedDeliveryAsync(customerId, orderId, itemId, kg: 500m);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryTask = Task.Run(async () =>
        {
            await ReadyAsync;
            return (await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null)).StatusCode;
        });
        var cancelTask = Task.Run(async () =>
        {
            await ReadyAsync;
            return (await Client.DeleteAsync($"/api/ready-goods/transfers/{transferId}?reason=race-with-delivery")).StatusCode;
        });

        start.SetResult();
        await Task.WhenAll(deliveryTask, cancelTask);
        var deliveryStatus = deliveryTask.Result;
        var cancelStatus = cancelTask.Result;

        (deliveryStatus == HttpStatusCode.OK || cancelStatus == HttpStatusCode.NoContent).Should().BeTrue("one operation wins");
        (deliveryStatus == HttpStatusCode.OK && cancelStatus == HttpStatusCode.NoContent).Should().BeFalse("they must serialize on the same lot");

        var finalBalance = await ReadyBalanceAsync(customerId, orderId);
        finalBalance.Should().Be(0m, "either the delivery consumed the lot or the cancellation reversed it - never negative, never double-counted");
    }
}

/// <summary>T7: two receipts race one invoice; combined amount exceeds the total.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class ReceiptOverpaymentRaceTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task Only_One_Receipt_Fits_The_Invoice_Total()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (customerId, itemId, _, _) = await SeedMasterDataAsync("t7");
        var account = (await (await Client.PostAsJsonAsync("/api/treasury-accounts", new { code = $"A{suffix}", name = "Main Cash", kind = TreasuryAccountKind.Cash }))
            .Content.ReadFromJsonAsync<TreasuryAccountDto>(Json))!;

        var invoice = (await (await Client.PostAsJsonAsync("/api/invoices", new
        {
            customerId, invoiceDate = DateTime.UtcNow, discount = 0m, tax = 0m,
            lines = new[] { new { itemId, quantity = 500m, processingPrice = 2m } } // total 1000
        })).Content.ReadFromJsonAsync<InvoiceDto>(Json))!;
        invoice = (await (await Client.PostAsync($"/api/invoices/{invoice.Id}/issue", null)).Content.ReadFromJsonAsync<InvoiceDto>(Json))!;

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await ReadyAsync;
            var response = await Client.PostAsJsonAsync("/api/receipts", new
            {
                receiptDate = DateTime.UtcNow, treasuryAccountId = account.Id, amount = 800m,
                customerId, invoiceId = invoice.Id
            });
            return response.StatusCode;
        })).ToList();

        start.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(1, "only 1000 of the 1600 requested fits");
        statuses.Count(s => s == HttpStatusCode.UnprocessableEntity).Should().Be(1, "the overpayment guard throws DomainException (422)");

        var receipts = (await Client.GetFromJsonAsync<List<ReceiptDto>>($"/api/receipts?customerId={customerId}", Json))!
            .Where(r => r.InvoiceId == invoice.Id && r.Status != TreasuryDocumentStatus.Cancelled.ToString())
            .ToList();
        receipts.Sum(r => r.Amount).Should().Be(800m, "exactly one receipt posted - no overpayment persisted");
    }
}

/// <summary>T8: MarkDelivered twice must deduct exactly once.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class DoubleDeliveryTests : SqlServerConcurrencyTestBase
{
    [Fact]
    public async Task MarkDelivered_Twice_Deducts_Exactly_Once()
    {
        var (customerId, itemId, rawWarehouseId, readyWarehouseId) = await SeedMasterDataAsync("t8");
        var messageId = await ReceiveAndAcceptAsync(customerId, itemId, rawWarehouseId, kg: 500m);
        var orderId = await CreateOrderAsync(customerId, itemId, 500m);
        await AllocateAsync(orderId, itemId, messageId, 500m);
        await ProduceAndTransferAsync(orderId, readyWarehouseId, inputKg: 500m, outputKg: 500m);

        var delivery = await CreatePreparedDeliveryAsync(customerId, orderId, itemId, kg: 500m);

        // First call delivers; the second must fail because the delivery is
        // already Delivered (state machine) - no second deduction possible.
        (await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync($"/api/deliveries/{delivery.Id}/deliver", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await ReadyBalanceAsync(customerId, orderId)).Should().Be(0m, "500 posted once, deducted exactly once");
    }
}

/// <summary>T9/T10: issued JWTs die with the account's active flag / permission set.</summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class JwtSessionInvalidationTests : SqlServerConcurrencyTestBase
{
    private async Task<(Guid UserId, string Token)> CreateAndLoginAsync(List<string> roles)
    {
        var username = $"u{Guid.NewGuid():N}"[..12];
        var createResponse = await Client.PostAsJsonAsync("/api/users", new
        { username, password = "P@ssw0rd-Test-123", displayName = "Session User", roles });
        createResponse.EnsureSuccessStatusCode();
        var user = (await createResponse.Content.ReadFromJsonAsync<UserDto>(Json))!;

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { username, password = "P@ssw0rd-Test-123" });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        return (user.Id, login.GetProperty("token").GetString()!);
    }

    private static async Task<HttpResponseMessage> CallAsUserAsync(HttpClient sharedClient, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await sharedClient.SendAsync(request);
    }

    [Fact]
    public async Task Deactivated_User_JWT_Is_Rejected_On_Next_Request()
    {
                // UsersController carries [Authorize(Roles = "admin")] at CLASS level
        // plus the Permission:users.manage policy on the endpoint - ASP.NET
        // requires BOTH, so the user must hold the admin role (class gate) AND
        // the users.manage permission claim (policy gate). With only
        // "users.manage" the class-level role gate returned 403 before the
        // permission check ever ran.
        var (userId, token) = await CreateAndLoginAsync(new List<string> { "admin", "users.manage" });

        (await CallAsUserAsync(Client, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.PostAsync($"/api/users/{userId}/deactivate", null)).EnsureSuccessStatusCode();

        (await CallAsUserAsync(Client, token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Permission_Change_Invalidates_Still_Valid_JWT()
    {
                // UsersController carries [Authorize(Roles = "admin")] at CLASS level
        // plus the Permission:users.manage policy on the endpoint - ASP.NET
        // requires BOTH, so the user must hold the admin role (class gate) AND
        // the users.manage permission claim (policy gate). With only
        // "users.manage" the class-level role gate returned 403 before the
        // permission check ever ran.
        var (userId, token) = await CreateAndLoginAsync(new List<string> { "admin", "users.manage" });

        (await CallAsUserAsync(Client, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await Client.PutAsJsonAsync($"/api/users/{userId}", new { displayName = "Session User", roles = new List<string>() });
        update.EnsureSuccessStatusCode();

        (await CallAsUserAsync(Client, token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
