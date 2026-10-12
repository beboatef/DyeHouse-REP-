using System.Net;
using System.Net.Http.Json;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Materials.DTOs;
using DyeHouseERP.Application.MaterialSales.DTOs;
using DyeHouseERP.Application.Supplies.DTOs;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// SQL Server integration cover for two defects that only a real database can settle.
///
/// 1. H-2: a line APPENDED to a document that is ALREADY persisted used to be saved as an
///    UPDATE against a row that was never inserted (EF change detection sees the
///    client-generated key and treats the child as Modified), so SaveChanges threw
///    DbUpdateConcurrencyException and the API answered 409 CONCURRENT_UPDATE - which is
///    exactly why these cases are asserted here, on the provider where the 409 was observed,
///    and not only against the in-memory provider.
///
/// 2. Warehouse deactivation: a deactivated warehouse never takes part in anything NEW - not
///    a new document, not a new stock movement, and not a line appended to a document that
///    would move stock - while its history stays readable and its balances stay untouched.
///    The material ledger (the source of truth for every balance asserted here) and the
///    sp_getapplock-serialized posting window have no in-memory equivalent.
///
/// A REAL SQL Server is REQUIRED (DYEHOUSE_TEST_CONNECTION). There is no skip, no fallback,
/// no Docker and no Testcontainers: when no server is reachable these tests fail with the
/// real connection error rather than pretending to pass. Each test seeds its OWN master data
/// with unique codes, and the factory recreates the schema per test class, so nothing here
/// depends on, or cleans up after, another test's rows.
/// </summary>
[Trait("Category", "Concurrency")]
[Collection("IntegrationDatabase")]
public class ChildInsertAndWarehouseGuardSqlTests : SqlServerConcurrencyTestBase
{
    /// <summary>Just the id of a created master record - keeps these tests off the DTO types they do not read.</summary>
    private sealed record IdView(Guid Id);

    /// <summary>The purchase order fields this suite asserts on (extra JSON fields are ignored).</summary>
    private sealed record PurchaseOrderView(Guid Id, string OrderNumber, List<PurchaseOrderLineView>? Lines);

    private sealed record PurchaseOrderLineView(Guid Id, Guid MaterialId, decimal Quantity);

    /// <summary>The material-transfer fields this suite asserts on.</summary>
    private sealed record MaterialTransferView(Guid Id, Guid MaterialId, decimal Quantity);

    // ------------------------------------------------------------------ seeding

    /// <summary>
    /// A material of the requested kind with REAL stock, received through the purchases receipt
    /// document so the balance comes from the append-only material ledger exactly as it would in
    /// production. Pass <paramref name="intoWarehouseId"/> to receive into a warehouse that
    /// already exists (a document's own warehouse, for instance) instead of a fresh one.
    /// Returns the material and the warehouse it now sits in.
    /// </summary>
    private async Task<(Guid MaterialId, Guid WarehouseId)> SeedMaterialStockAsync(
        string prefix, MaterialKind kind, WarehouseKind warehouseKind, decimal quantity,
        Guid? intoWarehouseId = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var supplier = (await (await Client.PostAsJsonAsync("/api/suppliers",
                new { code = $"{prefix}SUP{suffix}", name = "Hardening Supplier" }))
            .Content.ReadFromJsonAsync<IdView>(Json))!;

        var warehouseId = intoWarehouseId ?? (await (await Client.PostAsJsonAsync("/api/warehouses",
                new { code = $"{prefix}WH{suffix}", name = "Hardening Warehouse", kind = warehouseKind }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!.Id;

        var material = (await (await Client.PostAsJsonAsync("/api/materials", new
            {
                code = $"{prefix}M{suffix}", name = "Hardening Material",
                unit = MaterialUnit.KG, purchasePrice = 10m, kind
            })).Content.ReadFromJsonAsync<MaterialDto>(Json))!;

        var receipt = await Client.PostAsJsonAsync("/api/purchases/receipts", new
        {
            receiptDate = DateTime.UtcNow,
            supplierId = supplier.Id,
            warehouseId,
            supplierDocumentNumber = (string?)null,
            notes = (string?)null,
            lines = new[] { new { materialId = material.Id, quantity, unit = MaterialUnit.KG, unitCost = 10m, notes = (string?)null } }
        });
        receipt.EnsureSuccessStatusCode();

        (await MaterialBalanceAsync(material.Id)).Should().Be(quantity, "the receipt is what puts stock behind these tests");

        return (material.Id, warehouseId);
    }

    /// <summary>Creates an empty warehouse of the requested kind and returns its id.</summary>
    private async Task<Guid> CreateWarehouseAsync(string prefix, WarehouseKind kind)
    {
        var warehouse = (await (await Client.PostAsJsonAsync("/api/warehouses",
                new { code = $"{prefix}{Guid.NewGuid().ToString("N")[..8]}", name = "Hardening WH", kind }))
            .Content.ReadFromJsonAsync<WarehouseDto>(Json))!;
        return warehouse.Id;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The live on-hand figure the material ledger reports for one material.</summary>
    private async Task<decimal> MaterialBalanceAsync(Guid materialId)
    {
        var materials = await Client.GetFromJsonAsync<List<MaterialDto>>("/api/materials", Json);
        return materials!.Single(m => m.Id == materialId).CurrentBalance;
    }

    private async Task<SupplyIssueDto> CreateSupplyIssueAsync(Guid warehouseId)
        => (await (await Client.PostAsJsonAsync("/api/supplies", new
        {
            issueDate = DateTime.UtcNow, warehouseId, issuedTo = "Maintenance", purpose = "Spare parts"
        })).Content.ReadFromJsonAsync<SupplyIssueDto>(Json))!;

    private async Task<SupplyIssueDto> GetSupplyIssueAsync(Guid issueId)
        => (await Client.GetFromJsonAsync<SupplyIssueDto>($"/api/supplies/{issueId}", Json))!;

    private Task<HttpResponseMessage> AddSupplyLineAsync(Guid issueId, Guid materialId, decimal quantity)
        => Client.PostAsJsonAsync($"/api/supplies/{issueId}/lines", new { materialId, quantity });

    private Task<HttpResponseMessage> AddSaleLineAsync(Guid saleId, Guid materialId, decimal quantity)
        => Client.PostAsJsonAsync($"/api/material-sales/{saleId}/lines", new { materialId, quantity });

    private static async Task<string> AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "the refusal must reach the client as {0}, body was: {1}", expected, body);
        return body;
    }

    // ------------------------------------------------- H-2e / H-2f (line on a saved parent)

    [Fact]
    public async Task AddingLinesToAPersistedSupplyIssue_InsertsEachRowExactlyOnce()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("H2E", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 100m);

        // The second material must sit in the SAME warehouse: a supply issue may only draw from
        // the store it is written against, and the balance check is per warehouse.
        var (otherMaterialId, _) = await SeedMaterialStockAsync("H2E2", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 100m, intoWarehouseId: warehouseId);

        var issue = await CreateSupplyIssueAsync(warehouseId);
        issue.Lines.Should().BeEmpty("a draft is created without lines");

        // The issue is already saved when the first line is appended - the case that used to 409.
        var first = await AddSupplyLineAsync(issue.Id, materialId, 5m);
        first.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            "appending a line to a saved issue must INSERT, never answer CONCURRENT_UPDATE");
        first.EnsureSuccessStatusCode();

        var afterFirst = await GetSupplyIssueAsync(issue.Id);
        afterFirst.Lines.Should().HaveCount(1);
        afterFirst.Lines.Single().MaterialId.Should().Be(materialId);
        afterFirst.Lines.Single().Quantity.Should().Be(5m);
        var firstLineId = afterFirst.Lines.Single().Id;

        // A second, separate edit must add exactly one more row and leave the first untouched.
        (await AddSupplyLineAsync(issue.Id, otherMaterialId, 7m)).EnsureSuccessStatusCode();

        var afterSecond = await GetSupplyIssueAsync(issue.Id);
        afterSecond.Lines.Should().HaveCount(2, "each edit inserts exactly its own line - nothing duplicated, nothing lost");
        afterSecond.Lines.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        afterSecond.Lines.Should().ContainSingle(l => l.Id == firstLineId && l.Quantity == 5m,
            "the line added by the earlier edit survives unchanged");
        afterSecond.Lines.Should().ContainSingle(l => l.MaterialId == otherMaterialId && l.Quantity == 7m);
    }

    [Fact]
    public async Task AddingLinesToAPersistedMaterialSale_InsertsEachRowExactlyOnce()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("H2F", MaterialKind.Chemical,
            WarehouseKind.Materials, 100m);

        var sale = (await (await Client.PostAsJsonAsync("/api/material-sales", new
        {
            saleDate = DateTime.UtcNow, warehouseId, buyerName = "Hardening Buyer"
        })).Content.ReadFromJsonAsync<MaterialSaleDto>(Json))!;
        sale.Lines.Should().BeEmpty();

        var first = await AddSaleLineAsync(sale.Id, materialId, 3m);
        first.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            "appending a line to a saved sale must INSERT, never answer CONCURRENT_UPDATE");
        first.EnsureSuccessStatusCode();

        (await AddSaleLineAsync(sale.Id, materialId, 4m)).EnsureSuccessStatusCode();

        var reloaded = (await Client.GetFromJsonAsync<MaterialSaleDto>($"/api/material-sales/{sale.Id}", Json))!;
        reloaded.Lines.Should().HaveCount(2);
        reloaded.Lines.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        reloaded.Lines.Select(l => l.Quantity).Should().BeEquivalentTo(new[] { 3m, 4m });
    }

    // ---------------------------------------------------- H-2d (line on a saved order)

    [Fact]
    public async Task AddingALineToAPersistedPurchaseOrder_InsertsItOnceAndKeepsTheEarlierLine()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("H2D", MaterialKind.Chemical,
            WarehouseKind.Materials, 50m);
        var (secondMaterialId, _) = await SeedMaterialStockAsync("H2D2", MaterialKind.Chemical,
            WarehouseKind.Materials, 50m);

        var supplier = (await (await Client.PostAsJsonAsync("/api/suppliers",
                new { code = $"H2DSUP{Guid.NewGuid().ToString("N")[..8]}", name = "Order Supplier" }))
            .Content.ReadFromJsonAsync<IdView>(Json))!;

        var order = (await (await Client.PostAsJsonAsync("/api/purchases/orders", new
        {
            orderDate = DateTime.UtcNow, supplierId = supplier.Id, warehouseId,
            lines = new[] { new { materialId, quantity = 10m, unit = MaterialUnit.KG, unitPrice = 10m } }
        })).Content.ReadFromJsonAsync<PurchaseOrderView>(Json))!;
        order.Lines.Should().NotBeNull("the created order comes back with its line");
        order.Lines!.Should().HaveCount(1);

        var add = await Client.PostAsJsonAsync($"/api/purchases/orders/{order.Id}/lines", new
        {
            materialId = secondMaterialId, quantity = 4m, unit = MaterialUnit.KG, unitPrice = 12m
        });
        add.StatusCode.Should().NotBe(HttpStatusCode.Conflict,
            "appending an order line must INSERT, never answer CONCURRENT_UPDATE");
        add.EnsureSuccessStatusCode();

        var reloaded = (await Client.GetFromJsonAsync<PurchaseOrderView>($"/api/purchases/orders/{order.Id}", Json))!;
        reloaded.Lines.Should().NotBeNull();
        reloaded.Lines!.Should().HaveCount(2, "exactly one line was added and the existing line was kept");
        reloaded.Lines!.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        reloaded.Lines!.Should().ContainSingle(l => l.MaterialId == materialId && l.Quantity == 10m);
        reloaded.Lines!.Should().ContainSingle(l => l.MaterialId == secondMaterialId && l.Quantity == 4m);
    }

    // --------------------------------------------- deactivated warehouse: new movements

    [Fact]
    public async Task PostingADraftOnADeactivatedWarehouse_IsRefused_AndWritesNoMovement()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("GUARD", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 10m);
        var issue = await CreateSupplyIssueAsync(warehouseId);
        (await AddSupplyLineAsync(issue.Id, materialId, 5m)).EnsureSuccessStatusCode();
        var balanceBefore = await MaterialBalanceAsync(materialId);

        (await Client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        var post = await Client.PostAsync($"/api/supplies/{issue.Id}/post", null);
        var body = await AssertRefusedAsync(post, HttpStatusCode.UnprocessableEntity);
        body.Should().Contain("deactivated", "the refusal must come from the warehouse rule, not from another check");

        (await MaterialBalanceAsync(materialId)).Should().Be(balanceBefore,
            "a refused post writes no new ledger row");
        var reloaded = await GetSupplyIssueAsync(issue.Id);
        reloaded.Lines.Should().HaveCount(1, "the draft keeps its line and is not half-posted");
        reloaded.Status.Should().NotBe(SupplyIssueStatus.Posted);
        reloaded.Status.Should().NotBe(SupplyIssueStatus.Cancelled);
    }

    [Fact]
    public async Task AddingALineToADraftOnADeactivatedWarehouse_IsRefused()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("GUARD2", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 10m);
        var issue = await CreateSupplyIssueAsync(warehouseId);

        (await Client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        var addLine = await AddSupplyLineAsync(issue.Id, materialId, 5m);
        var body = await AssertRefusedAsync(addLine, HttpStatusCode.UnprocessableEntity);
        body.Should().Contain("deactivated");

        (await GetSupplyIssueAsync(issue.Id)).Lines.Should().BeEmpty(
            "a line that would move stock out of a deactivated warehouse is never recorded");
    }

    [Fact]
    public async Task MaterialTransferWithADeactivatedEnd_IsRefused_AndWritesNoMovement()
    {
        var (materialId, fromWarehouseId) = await SeedMaterialStockAsync("GUARD3", MaterialKind.Chemical,
            WarehouseKind.Materials, 20m);
        var toWarehouseId = await CreateWarehouseAsync("FROMME", WarehouseKind.Materials);
        var balanceBefore = await MaterialBalanceAsync(materialId);

        (await Client.PostAsJsonAsync($"/api/warehouses/{toWarehouseId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        var intoDeactivated = await Client.PostAsJsonAsync("/api/material-transfers", new
        {
            materialId, fromWarehouseId, toWarehouseId, quantity = 5m, notes = (string?)null
        });
        var body = await AssertRefusedAsync(intoDeactivated, HttpStatusCode.UnprocessableEntity);
        body.Should().Contain("deactivated");

        // The other direction is refused too: a deactivated warehouse cannot be emptied either.
        (await Client.PostAsJsonAsync($"/api/warehouses/{toWarehouseId}/active", new { isActive = true }))
            .EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync($"/api/warehouses/{fromWarehouseId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        var outOfDeactivated = await Client.PostAsJsonAsync("/api/material-transfers", new
        {
            materialId, fromWarehouseId, toWarehouseId, quantity = 5m, notes = (string?)null
        });
        await AssertRefusedAsync(outOfDeactivated, HttpStatusCode.UnprocessableEntity);

        (await MaterialBalanceAsync(materialId)).Should().Be(balanceBefore,
            "neither refusal may leave half a transfer in the ledger");

        var transfers = await Client.GetFromJsonAsync<List<MaterialTransferView>>("/api/material-transfers", Json);
        transfers!.Should().NotContain(t => t.MaterialId == materialId, "no transfer document was created");

        // An unknown warehouse is a not-found, not a business refusal.
        var unknown = await Client.PostAsJsonAsync("/api/material-transfers", new
        {
            materialId, fromWarehouseId = Guid.NewGuid(), toWarehouseId, quantity = 5m, notes = (string?)null
        });
        await AssertRefusedAsync(unknown, HttpStatusCode.NotFound);
    }

    // ------------------------------------------- deactivated warehouse: history survives

    [Fact]
    public async Task DeactivatingAWarehouse_KeepsHistoryBalancesAndListingIntact()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("HIST", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 10m);
        var issue = await CreateSupplyIssueAsync(warehouseId);
        (await AddSupplyLineAsync(issue.Id, materialId, 4m)).EnsureSuccessStatusCode();
        (await Client.PostAsync($"/api/supplies/{issue.Id}/post", null)).EnsureSuccessStatusCode();

        var balanceAfterPost = await MaterialBalanceAsync(materialId);
        balanceAfterPost.Should().Be(6m);

        (await Client.PostAsJsonAsync($"/api/warehouses/{warehouseId}/active", new { isActive = false }))
            .EnsureSuccessStatusCode();

        // Posted documents, their lines and the ledger they wrote are all still readable.
        var posted = await GetSupplyIssueAsync(issue.Id);
        posted.Status.Should().Be(SupplyIssueStatus.Posted);
        posted.Lines.Should().HaveCount(1);
        posted.Lines.Single().Quantity.Should().Be(4m);
        (await MaterialBalanceAsync(materialId)).Should().Be(balanceAfterPost,
            "deactivation is a flag: it never rewrites or zeroes a balance");

        // The warehouse is still listed, with its state - an administrator can see and restore it.
        var warehouses = await Client.GetFromJsonAsync<List<WarehouseDto>>("/api/warehouses", Json);
        warehouses!.Should().ContainSingle(w => w.Id == warehouseId && !w.IsActive);

        var activeOnly = await Client.GetFromJsonAsync<List<WarehouseDto>>("/api/warehouses?activeOnly=true", Json);
        activeOnly!.Should().NotContain(w => w.Id == warehouseId,
            "a deactivated warehouse is not offered for new documents");
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task TwoParallelPostsThatTogetherExceedStock_CannotBothSucceed()
    {
        var (materialId, warehouseId) = await SeedMaterialStockAsync("RACE", MaterialKind.OperatingSupply,
            WarehouseKind.OperatingSupplies, 10m);

        var first = await CreateSupplyIssueAsync(warehouseId);
        var second = await CreateSupplyIssueAsync(warehouseId);
        (await AddSupplyLineAsync(first.Id, materialId, 8m)).EnsureSuccessStatusCode();
        (await AddSupplyLineAsync(second.Id, materialId, 8m)).EnsureSuccessStatusCode();

        // Both drafts are legal on their own (8 <= 10); posting them at the same time is not.
        var responses = await Task.WhenAll(
            Client.PostAsync($"/api/supplies/{first.Id}/post", null),
            Client.PostAsync($"/api/supplies/{second.Id}/post", null));

        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1,
            "the stock lock serializes the two posts and only one of them can cover the 8 units");

        var balance = await MaterialBalanceAsync(materialId);
        balance.Should().Be(2m, "exactly one 8-unit issue left the store");
        balance.Should().BeGreaterThanOrEqualTo(0m, "stock may never go negative");

        var issues = await Task.WhenAll(GetSupplyIssueAsync(first.Id), GetSupplyIssueAsync(second.Id));
        issues.Count(i => i.Status == SupplyIssueStatus.Posted).Should().Be(1,
            "the losing post is refused, not half-applied");
        issues.SelectMany(i => i.Lines).Should().HaveCount(2, "one line per draft, no duplicated rows");
    }
}
