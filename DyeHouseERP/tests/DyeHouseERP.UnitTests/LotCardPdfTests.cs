using System.Reflection;
using System.Security.Claims;
using System.Text;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Inventory.DTOs;
using DyeHouseERP.Application.Inventory.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// LOT TRACEABILITY CARD as a PDF (بطاقة تتبع اللوط) - the printable document for ONE lot.
///
/// Three halves, all reachable without a browser and without SQL Server:
///   * the CONTRACT the endpoint enforces - a lot has to be named by an identifier its own warehouse
///     uses, and a request that resolves to no lot, to a lot of another warehouse, or to SEVERAL
///     independent balances is refused instead of printing whichever one came first;
///   * the DOCUMENT the controller asks the renderer for - its columns, its row/header alignment, every
///     movement row naming its own item, the cumulative balance being the query's own figure, the
///     chronological order an official ledger is read in, and KG/Meter staying independent;
///   * the FILE that comes out - real bytes from the production renderer, the PDF magic, the content
///     type and a download name derived from the lot number.
///
/// The rules these pin, because getting them wrong is how a printed document lies:
///   * one card is one lot: a receipt message carrying two items is TWO lots, so the card never mixes
///     their balances and never prints a combined total beside a single item's name;
///   * a missing identifier, or an identifier of the wrong kind for the scope, is a 400 and NO file -
///     never "the first lot that happened to match";
///   * the card is never WINDOWED - only the lot's identity is accepted, never a date or movement-kind
///     filter, so its rows always add up to its own closing balance;
///   * a unit the lot never recorded prints "—", never a zero;
///   * generating the card is READ-ONLY.
/// </summary>
public class LotCardPdfTests
{
    private static readonly Guid LotId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ItemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherItemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WarehouseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherWarehouseId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static string RawLotKey(Guid messageId, Guid itemId) => $"R|{messageId}|{itemId}|{Guid.Empty}";
    private static string MaterialLotKey(Guid materialId, Guid warehouseId) => $"M|{materialId}|{warehouseId}";

    /// <summary>One raw lot: a receipt message carrying exactly one item, with meter recorded.</summary>
    private static StockTraceabilityDto RawLot(bool withMeter)
    {
        var lotKey = RawLotKey(LotId, ItemId);
        var dto = new StockTraceabilityDto
        {
            Scope = StockScopes.Raw,
            GeneratedAtUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc),
            TotalRemainingKg = 400m,
            TotalRemainingMeter = withMeter ? 820m : null,
            TotalReceivedKg = 1000m,
            TotalDeliveredKg = 600m,
            MovementCount = 2
        };

        dto.Rows.Add(new StockTraceRowDto
        {
            Scope = StockScopes.Raw, LotKey = lotKey, ItemId = ItemId,
            ItemCode = "FAB-1", ItemName = "Cotton", BaseUnit = "KG",
            RawMessageId = LotId, MessageNumber = "MSG-2026-000007", MessageDate = new DateTime(2026, 10, 1),
            MessageStatus = "PartiallyUsed",
            CustomerCode = "C-1", CustomerName = "Customer", WarehouseName = "Raw Material",
            RemainingKg = 400m, RemainingMeter = withMeter ? 820m : null, MovementCount = 2
        });

        // Newest first, exactly as the query returns them: the card has to re-order them chronologically.
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotKey, Date = new DateTime(2026, 10, 9),
            DocumentType = "ProductionOrder", DocumentNumber = "PRD-2026-000003",
            Kind = "Issued", Direction = "Out", ProductionOrderId = OrderId, OrderNumber = "PRD-2026-000003",
            ItemId = ItemId, ItemCode = "FAB-1", ItemName = "Cotton",
            QuantityKg = 600m, QuantityMeter = withMeter ? 1200m : null, Unit = "KG / Meter",
            BalanceAfterKg = 400m, BalanceAfterMeter = withMeter ? 820m : null,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 9, 8, 0, 0)
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotKey, Date = new DateTime(2026, 10, 1),
            DocumentType = "RawReceiptMessage", DocumentNumber = "MSG-2026-000007",
            Kind = "Received", Direction = "In", RawMessageId = LotId, MessageNumber = "MSG-2026-000007",
            ItemId = ItemId, ItemCode = "FAB-1", ItemName = "Cotton",
            QuantityKg = 1000m, QuantityMeter = withMeter ? 2020m : null, Unit = "KG / Meter",
            BalanceAfterKg = 1000m, BalanceAfterMeter = withMeter ? 2020m : null,
            CreatedBy = "clerk", CreatedAtUtc = new DateTime(2026, 10, 1, 7, 0, 0)
        });

        return dto;
    }

    /// <summary>
    /// ONE raw receipt message carrying TWO items - two independent balances. The message-wide totals are
    /// the sum of both, which is exactly the figure a card must never print beside one item's name.
    /// </summary>
    private static StockTraceabilityDto RawMessageWithTwoItems()
    {
        var lotA = RawLotKey(LotId, ItemId);
        var lotB = RawLotKey(LotId, OtherItemId);

        var dto = new StockTraceabilityDto
        {
            Scope = StockScopes.Raw,
            GeneratedAtUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc),
            TotalRemainingKg = 1300m,
            TotalRemainingMeter = 300m,
            MovementCount = 3
        };

        dto.Rows.Add(new StockTraceRowDto
        {
            Scope = StockScopes.Raw, LotKey = lotA, ItemId = ItemId,
            ItemCode = "IT-A", ItemName = "Cotton", BaseUnit = "KG", SecondaryUnit = "Meter",
            RawMessageId = LotId, MessageNumber = "MSG-2026-000007",
            CustomerCode = "C-1", CustomerName = "Customer", WarehouseName = "Raw Material",
            RemainingKg = 600m, RemainingMeter = 300m, MovementCount = 2
        });
        dto.Rows.Add(new StockTraceRowDto
        {
            Scope = StockScopes.Raw, LotKey = lotB, ItemId = OtherItemId,
            ItemCode = "IT-B", ItemName = "Polyester", BaseUnit = "KG",
            RawMessageId = LotId, MessageNumber = "MSG-2026-000007",
            CustomerCode = "C-1", CustomerName = "Customer", WarehouseName = "Raw Material",
            RemainingKg = 700m, RemainingMeter = null, MovementCount = 1
        });

        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotA, Date = new DateTime(2026, 10, 3),
            DocumentType = "ProductionOrder", DocumentNumber = "PRD-1", Kind = "Issued", Direction = "Out",
            ItemId = ItemId, ItemCode = "IT-A", ItemName = "Cotton", RawMessageId = LotId,
            QuantityKg = 400m, QuantityMeter = 200m, BalanceAfterKg = 600m, BalanceAfterMeter = 300m,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 3, 8, 0, 0)
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotA, Date = new DateTime(2026, 10, 1),
            DocumentType = "RawReceiptMessage", DocumentNumber = "MSG-2026-000007", Kind = "Received", Direction = "In",
            ItemId = ItemId, ItemCode = "IT-A", ItemName = "Cotton", RawMessageId = LotId,
            QuantityKg = 1000m, QuantityMeter = 500m, BalanceAfterKg = 1000m, BalanceAfterMeter = 500m,
            CreatedBy = "clerk", CreatedAtUtc = new DateTime(2026, 10, 1, 7, 0, 0)
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotB, Date = new DateTime(2026, 10, 1),
            DocumentType = "RawReceiptMessage", DocumentNumber = "MSG-2026-000007", Kind = "Received", Direction = "In",
            ItemId = OtherItemId, ItemCode = "IT-B", ItemName = "Polyester", RawMessageId = LotId,
            QuantityKg = 700m, BalanceAfterKg = 700m,
            CreatedBy = "clerk", CreatedAtUtc = new DateTime(2026, 10, 1, 7, 5, 0)
        });

        return dto;
    }

    /// <summary>
    /// A raw lot whose ledger nets to EXACTLY ZERO, on an item that has no meter at all: received 500 KG
    /// and issued all 500.
    /// </summary>
    private static StockTraceabilityDto RawLotBalancedToZero()
    {
        var lotKey = RawLotKey(LotId, ItemId);
        var dto = new StockTraceabilityDto
        {
            Scope = StockScopes.Raw,
            GeneratedAtUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc),
            TotalRemainingKg = 0m, TotalRemainingMeter = null, MovementCount = 2
        };
        dto.Rows.Add(new StockTraceRowDto
        {
            Scope = StockScopes.Raw, LotKey = lotKey, ItemId = ItemId,
            ItemCode = "FAB-1", ItemName = "Cotton", BaseUnit = "KG",
            RawMessageId = LotId, MessageNumber = "MSG-2026-000008", MessageDate = new DateTime(2026, 10, 1),
            MessageStatus = "Depleted", CustomerCode = "C-1", CustomerName = "Customer",
            WarehouseName = "Raw Material",
            RemainingKg = 0m, RemainingMeter = null, MovementCount = 2
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotKey, Date = new DateTime(2026, 10, 3),
            DocumentType = "ProductionOrder", DocumentNumber = "PRD-2026-000004", Kind = "Issued",
            Direction = "Out", ItemId = ItemId, ItemCode = "FAB-1", ItemName = "Cotton",
            QuantityKg = 500m, BalanceAfterKg = 0m,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 3, 8, 0, 0)
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, LotKey = lotKey, Date = new DateTime(2026, 10, 1),
            DocumentType = "RawReceiptMessage", DocumentNumber = "MSG-2026-000008", Kind = "Received",
            Direction = "In", RawMessageId = LotId, MessageNumber = "MSG-2026-000008",
            ItemId = ItemId, ItemCode = "FAB-1", ItemName = "Cotton",
            QuantityKg = 500m, BalanceAfterKg = 500m,
            CreatedBy = "clerk", CreatedAtUtc = new DateTime(2026, 10, 1, 7, 0, 0)
        });
        return dto;
    }

    private static StockTraceabilityDto MaterialsLot()
    {
        var dto = new StockTraceabilityDto
        {
            Scope = StockScopes.Materials,
            GeneratedAtUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc),
            TotalRemainingQuantity = 70m, TotalReceivedQuantity = 100m, TotalIssuedQuantity = 30m, MovementCount = 1
        };
        dto.Rows.Add(new StockTraceRowDto
        {
            Scope = StockScopes.Materials, LotKey = MaterialLotKey(LotId, WarehouseId), MaterialId = LotId,
            MaterialCode = "MAT-1", MaterialName = "Dye", MaterialUnit = "KG", MaterialKind = "Chemical",
            WarehouseId = WarehouseId, WarehouseCode = "WH-CHEM", WarehouseName = "Chemicals",
            RemainingQuantity = 70m
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Materials, LotKey = MaterialLotKey(LotId, WarehouseId),
            Date = new DateTime(2026, 10, 2),
            DocumentType = "SupplyIssue", DocumentNumber = "SUP-2026-000004",
            Kind = "Issued", Direction = "Out", MaterialId = LotId, MaterialCode = "MAT-1",
            WarehouseId = WarehouseId, Quantity = 30m, Unit = "KG", BalanceAfter = 70m,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 2, 9, 0, 0)
        });
        return dto;
    }

    /// <summary>The same material in TWO warehouses: two independent balances behind one identifier.</summary>
    private static StockTraceabilityDto MaterialInTwoWarehouses()
    {
        var dto = new StockTraceabilityDto
        {
            Scope = StockScopes.Materials,
            GeneratedAtUtc = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc),
            TotalRemainingQuantity = 140m, MovementCount = 2
        };
        foreach (var (warehouseId, warehouseCode, warehouseName, quantity) in new[]
                 {
                     (WarehouseId, "WH-C1", "Chemicals 1", 100m), (OtherWarehouseId, "WH-C2", "Chemicals 2", 40m)
                 })
        {
            dto.Rows.Add(new StockTraceRowDto
            {
                Scope = StockScopes.Materials, LotKey = MaterialLotKey(LotId, warehouseId), MaterialId = LotId,
                MaterialCode = "MAT-1", MaterialName = "Dye", MaterialUnit = "KG",
                WarehouseId = warehouseId, WarehouseCode = warehouseCode, WarehouseName = warehouseName,
                RemainingQuantity = quantity
            });
            dto.Movements.Add(new StockTraceMovementDto
            {
                Id = Guid.NewGuid(), Scope = StockScopes.Materials, LotKey = MaterialLotKey(LotId, warehouseId),
                Date = new DateTime(2026, 10, 1), DocumentType = "PurchaseReceipt", DocumentNumber = "PR-1",
                Kind = "Received", Direction = "In", MaterialId = LotId, MaterialCode = "MAT-1",
                WarehouseId = warehouseId, Quantity = quantity, Unit = "KG", BalanceAfter = quantity,
                CreatedBy = "clerk", CreatedAtUtc = new DateTime(2026, 10, 1, 7, 0, 0)
            });
        }
        return dto;
    }

    /// <summary>
    /// The controller with a stub mediator that answers the prepared ledger read, narrows it the way the
    /// query narrows a real one, and RECORDS every query it was asked for - that record is the evidence
    /// for "the card named the lot it was asked for, and was never windowed", and its emptiness is the
    /// evidence that a refused request asked nothing at all.
    ///
    /// The stub has to apply the identity filters itself, because the controller deliberately relies on
    /// the QUERY to resolve an identity to rows: without that, every request would see every row and the
    /// only testable outcome would be the refusal.
    /// </summary>
    private static (InventoryTraceabilityController Controller, List<GetStockTraceabilityQuery> Sent)
        BuildController(StockTraceabilityDto data)
    {
        var sent = new List<GetStockTraceabilityQuery>();
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetStockTraceabilityQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<StockTraceabilityDto>, CancellationToken>((query, _) => sent.Add((GetStockTraceabilityQuery)query))
            .Returns<GetStockTraceabilityQuery, CancellationToken>((query, _) => Task.FromResult(Narrow(data, query)));

        return (new InventoryTraceabilityController(sender.Object), sent);
    }

    /// <summary>
    /// The query's own identity rules, applied to the prepared read: a row is kept when every supplied
    /// identifier it carries matches, movements follow the LotKey of a kept row, and the totals are
    /// re-summed over what remains. This is what makes "ask for one item of a two-item message" a
    /// realistic request against this double.
    /// </summary>
    private static StockTraceabilityDto Narrow(StockTraceabilityDto data, GetStockTraceabilityQuery query)
    {
        var rows = data.Rows.Where(r =>
            (query.ItemId is null || r.ItemId == query.ItemId) &&
            (query.MaterialId is null || r.MaterialId == query.MaterialId) &&
            (query.WarehouseId is null || r.WarehouseId == query.WarehouseId) &&
            (query.RawMessageId is null || r.RawMessageId == query.RawMessageId) &&
            (query.ProductionOrderId is null || r.ProductionOrderId == query.ProductionOrderId) &&
            (query.CustomerId is null || r.CustomerId == query.CustomerId)).ToList();

        var keys = rows.Select(r => r.LotKey).ToHashSet();
        var movements = data.Movements.Where(m => keys.Contains(m.LotKey)).ToList();

        decimal? Sum(Func<StockTraceRowDto, decimal?> pick) =>
            rows.Any(r => pick(r).HasValue) ? rows.Sum(r => pick(r) ?? 0m) : null;

        return new StockTraceabilityDto
        {
            Scope = query.Scope,
            Rows = rows,
            Movements = movements,
            TotalRemainingKg = Sum(r => r.RemainingKg),
            TotalRemainingMeter = Sum(r => r.RemainingMeter),
            TotalRemainingQuantity = Sum(r => r.RemainingQuantity),
            TotalReceivedKg = Sum(r => r.ReceivedKg),
            TotalDeliveredKg = Sum(r => r.DeliveredKg),
            MovementCount = movements.Count,
            GeneratedAtUtc = data.GeneratedAtUtc
        };
    }

    // ------------------------------------------------------- naming the lot: the API contract

    [Fact]
    public async Task TheCard_IsNeverWindowed_OnlyTheLotsIdentityReachesTheQuery()
    {
        var (controller, sent) = BuildController(RawLot(withMeter: true));

        await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, new CapturingReportExportService());

        var query = sent.Should().ContainSingle().Subject;
        query.Scope.Should().Be(StockScopes.Raw);
        query.RawMessageId.Should().Be(LotId, "the lot is named by the identity its warehouse uses");
        query.Kind.Should().BeNull("a movement-kind filter would shorten the list without changing the balance");
        query.DocumentNumber.Should().BeNull();
        query.From.Should().BeNull();
        query.To.Should().BeNull();
        query.OnlyWithBalance.Should().BeFalse();
        query.IncludeDelivered.Should().BeTrue("a fully delivered lot still carries its history");
    }

    [Fact]
    public async Task WithoutAnyIdentifier_TheRequestIsRefused_AndNoFileIsProduced()
    {
        var (controller, sent) = BuildController(RawLot(withMeter: true));
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(StockScopes.Raw, null, null, null, null, null, null, export);

        result.Should().BeOfType<BadRequestObjectResult>("a card has to name its lot");
        sent.Should().BeEmpty("nothing is looked up when the lot was never named");
        export.PdfCalls.Should().Be(0, "a refused request must never produce a document");
    }

    [Fact]
    public async Task AnIdentifierFromAnotherWarehouse_IsRefused_NotSilentlyIgnored()
    {
        // A job order names a READY lot; on a raw card it identifies nothing. Silently dropping it is how
        // a caller ends up downloading the first raw lot there is.
        var (controller, sent) = BuildController(RawLot(withMeter: true));
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(StockScopes.Raw, null, OrderId, null, null, null, null, export);

        result.Should().BeOfType<BadRequestObjectResult>();
        sent.Should().BeEmpty("the conflicting identifier is refused before anything is read");
        export.PdfCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnIdentifierTheScopeCannotUse_IsRefused_ForMaterialsToo()
    {
        // Materials are not fabric items, so an item id can never name a materials lot.
        var (controller, sent) = BuildController(MaterialsLot());
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(StockScopes.Materials, null, null, LotId, ItemId, null, null, export);

        result.Should().BeOfType<BadRequestObjectResult>();
        sent.Should().BeEmpty();
        export.PdfCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnUnknownScope_IsRefused()
    {
        var (controller, sent) = BuildController(RawLot(withMeter: true));
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard("warehouse-7", LotId, null, null, null, null, null, export);

        result.Should().BeOfType<BadRequestObjectResult>();
        sent.Should().BeEmpty();
        export.PdfCalls.Should().Be(0);
    }

    [Fact]
    public async Task AMessageCarryingTwoItems_WithoutTheItem_IsRefusedAsAmbiguous_AndNamesBothLots()
    {
        // The identifier resolves to TWO independent balances. Printing the first would be a card for a
        // lot nobody named - and its stock would be one item's, its movements both items'.
        var (controller, sent) = BuildController(RawMessageWithTwoItems());
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, export);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        body.Should().Contain("AMBIGUOUS_LOT");
        body.Should().Contain("IT-A").And.Contain("IT-B", "both lots are named so the caller can pick one");
        body.Should().Contain("itemId", "and so is the identifier that picks one of them");
        export.PdfCalls.Should().Be(0, "a refusal that still wrote a document would be the bug it refuses");

        var query = sent.Should().ContainSingle().Subject;
        query.RawMessageId.Should().Be(LotId);
        query.ItemId.Should().BeNull("the caller asked for the message, not for one of its items");
    }

    [Fact]
    public async Task AMaterialStockedInTwoWarehouses_IsRefusedUntilTheWarehouseIsNamed()
    {
        var (controller, sent) = BuildController(MaterialInTwoWarehouses());
        var export = new CapturingReportExportService();

        var refused = await controller.ExportLotCard(StockScopes.Materials, null, null, LotId, null, null, null, export);
        refused.Should().BeOfType<BadRequestObjectResult>();
        export.PdfCalls.Should().Be(0);

        var named = await controller.ExportLotCard(
            StockScopes.Materials, null, null, LotId, null, OtherWarehouseId, null, new CapturingReportExportService());
        named.Should().BeOfType<FileContentResult>();
        sent.Last().WarehouseId.Should().Be(OtherWarehouseId, "the warehouse is half of a materials lot's identity");
    }

    // ------------------------------------------------------- one lot, one document

    [Fact]
    public async Task TheCardForOneItem_PrintsThatItemsStockOnly_AndNeverTheMessagesCombinedTotal()
    {
        var (controller, _) = BuildController(RawMessageWithTwoItems());
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, OtherItemId, null, null, export);

        result.Should().BeOfType<FileContentResult>();
        export.Subtitle.Should().Contain("IT-B", "the card names the item that was asked for");
        export.Subtitle.Should().Contain("700", "and its own closing balance");
        export.Subtitle.Should().NotContain("1300", "the message's combined balance is not this lot's stock");
        export.Subtitle.Should().NotContain("IT-A", "the sibling item must not appear on this card");

        // Only this lot's movements: the receipt of item B, and nothing of item A's receipt or issue.
        export.Rows.Should().HaveCount(1);
        export.Rows.Single()[1].Should().Be("استلام");
        export.Rows.Single()[2].Should().Be("IT-B - Polyester", "every movement row names its own item");
        export.Rows.Single()[6].Should().Be("700");
    }

    [Fact]
    public async Task TheCardForTheOtherItem_IsADifferentDocument()
    {
        var (controller, _) = BuildController(RawMessageWithTwoItems());
        var export = new CapturingReportExportService();

        await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, ItemId, null, null, export);

        export.Subtitle.Should().Contain("IT-A");
        export.Subtitle.Should().NotContain("IT-B");
        export.Subtitle.Should().Contain("600 / 300", "KG and Meter stay independent in the closing balance");
        export.Rows.Should().HaveCount(2);
        export.Rows.Should().OnlyContain(row => (string)row[2]! == "IT-A - Cotton");
        export.Rows[0][6].Should().Be("1,000 / 500", "the running balance is this lot's own");
        export.Rows[1][6].Should().Be("600 / 300");
    }

    [Fact]
    public async Task EveryRow_KeepsStepWithItsHeaders_IncludingTheCumulativeBalance()
    {
        var (controller, _) = BuildController(RawLot(withMeter: true));
        var export = new CapturingReportExportService();

        await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, export);

        export.Headers.Should().ContainInOrder(
            "التاريخ", "نوع الحركة", "الصنف", "المستند", "الاتجاه", "الكمية (كجم / متر)", "الرصيد التراكمي", "المستخدم");

        export.Rows.Should().HaveCount(2);
        foreach (var row in export.Rows)
            row.Should().HaveCount(export.Headers.Count, "a card with a misaligned table is not a document");

        // Chronological (oldest first): the cumulative column has to accumulate down the sheet.
        export.Rows[0][0].Should().Be("2026-10-01");
        export.Rows[0][1].Should().Be("استلام");
        export.Rows[0][4].Should().Be("وارد");
        export.Rows[0][6].Should().Be("1,000 / 2,020", "the balance after the receipt is the query's own figure");

        export.Rows[1][0].Should().Be("2026-10-09");
        export.Rows[1][1].Should().Be("صرف للتشغيل");
        export.Rows[1][4].Should().Be("صادر");
        export.Rows[1][6].Should().Be("400 / 820", "the closing row's balance is the lot's live balance");
    }

    [Fact]
    public async Task ALotThatNeverRecordedMeter_PrintsNoMeterFigure()
    {
        var (controller, _) = BuildController(RawLot(withMeter: false));
        var export = new CapturingReportExportService();

        await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, export);

        export.Rows.SelectMany(r => r.Where(c => c is string).Cast<string>())
            .Should().NotContain(v => v.Contains('/'), "with no meter recorded the cell is a single KG figure, never half-empty");

        export.Rows[0][5].Should().Be("1,000");
        export.Rows[0][6].Should().Be("1,000");
    }

    [Fact]
    public async Task ALotBalancedToZero_PrintsAZero_WhileAUnitItNeverRecorded_PrintsNothing()
    {
        var (controller, _) = BuildController(RawLotBalancedToZero());
        var export = new CapturingReportExportService();

        await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, export);

        // An emptied lot has a real balance of ZERO, and the closing movement's balance is that zero.
        export.Subtitle.Should().Contain("الرصيد الختامي: 0", "empty stock is a figure, not an absence");
        export.Rows[1][6].Should().Be("0", "a genuine zero is printed, never a dash");
        export.Rows[1][5].Should().Be("500");

        // The meter the item does NOT have stays absent: it is neither a zero nor an empty second figure.
        export.Rows.SelectMany(r => r.Where(c => c is string).Cast<string>())
            .Should().NotContain(v => v.Contains('/'), "no meter column is invented for a lot that never recorded one");
        export.Subtitle.Should().NotContain("0 /", "and the absent unit is certainly not shown as zero");
    }

    [Fact]
    public async Task AMaterialsLot_PrintsItsQuantityInTheMaterialsOwnUnit()
    {
        var (controller, sent) = BuildController(MaterialsLot());
        var export = new CapturingReportExportService();

        await controller.ExportLotCard(StockScopes.Materials, null, null, LotId, null, null, null, export);

        sent.Single().MaterialId.Should().Be(LotId);
        export.Headers.Should().ContainInOrder("المادة", "الكمية", "الرصيد التراكمي");
        export.Rows.Single()[2].Should().Be("MAT-1", "a movement row names its own material");
        export.Rows.Single()[5].Should().Be("30 KG", "the material's own unit is carried, never forced to KG/Meter");
        export.Rows.Single()[6].Should().Be("70");
    }

    [Fact]
    public async Task AnUnknownOrUnreachableLot_IsANotFound_NotAnEmptyDocument()
    {
        var (controller, _) = BuildController(new StockTraceabilityDto { Scope = StockScopes.Raw });
        var export = new CapturingReportExportService();

        var result = await controller.ExportLotCard(StockScopes.Raw, LotId, null, null, null, null, null, export);

        result.Should().BeOfType<NotFoundObjectResult>("an empty card would look like a real lot with no movements");
        export.PdfCalls.Should().Be(0);
    }

    // ------------------------------------------------------- the file

    [Fact]
    public async Task TheDownload_IsARealPdf_CarryingTheLotsNumberInItsName()
    {
        var (controller, _) = BuildController(RawLot(withMeter: true));
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة تجريبية", "Test Co"));

        var file = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, null, null, null, export);

        Encoding.ASCII.GetString(file.FileContents, 0, 5).Should().Be("%PDF-",
            "the response is a genuine PDF, never HTML or JSON wearing a .pdf name");
        file.FileContents.Length.Should().BeGreaterThan(1000);
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be("lot-card-MSG-2026-000007-FAB-1.pdf",
            "the name carries the identity that makes the card ONE lot: the message AND its item");
    }

    [Fact]
    public async Task ALotNumberWithUnsafeCharacters_StillYieldsASafeFileName()
    {
        var data = RawLot(withMeter: true);
        data.Rows[0].MessageNumber = "MSG/2026/000007\u0000";
        var (controller, _) = BuildController(data);
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));

        var file = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, null, null, null, export);

        file.FileDownloadName.Should().Be("lot-card-MSG-2026-000007-FAB-1.pdf",
            "a path separator or control character can never reach the download name");
        file.FileDownloadName.Should().NotContain("/").And.NotContain("\\");
    }

    [Fact]
    public async Task TwoItemsOfOneReceiptMessage_DownloadUnderTwoDifferentFileNames()
    {
        // The two items of ONE receipt message are two lots. Naming the file after the message alone made
        // both download as "lot-card-MSG-2026-000007.pdf", so the second silently replaced the first in the
        // user's Downloads folder. The item is half of the lot's identity, so it is half of its name.
        var (controller, _) = BuildController(RawMessageWithTwoItems());
        var exportA = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));
        var exportB = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));

        var cardA = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, ItemId, null, null, exportA);
        var cardB = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, OtherItemId, null, null, exportB);

        cardA.FileDownloadName.Should().Be("lot-card-MSG-2026-000007-IT-A.pdf");
        cardB.FileDownloadName.Should().Be("lot-card-MSG-2026-000007-IT-B.pdf");
        cardA.FileDownloadName.Should().NotBe(cardB.FileDownloadName,
            "one message carrying two items is two lots, and two cards must never share one file name");
    }

    [Fact]
    public async Task OneMaterialInTwoWarehouses_DownloadsUnderTwoDifferentFileNames()
    {
        // A materials lot is (material, warehouse). The material code is the same on both cards, so the
        // warehouse is what has to tell the two file names apart.
        var (controller, _) = BuildController(MaterialInTwoWarehouses());
        var exportFirst = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));
        var exportSecond = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));

        var first = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Materials, null, null, LotId, null, WarehouseId, null, exportFirst);
        var second = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Materials, null, null, LotId, null, OtherWarehouseId, null, exportSecond);

        first.FileDownloadName.Should().Be("lot-card-MAT-1-WH-C1.pdf");
        second.FileDownloadName.Should().Be("lot-card-MAT-1-WH-C2.pdf");
        first.FileDownloadName.Should().NotBe(second.FileDownloadName,
            "one material in two warehouses is two lots, not one file name for both");
    }

    [Fact]
    public async Task ALotWithNoUsableDisambiguator_DropsThatPartInsteadOfPrintingAPlaceholder()
    {
        // Nothing usable survives sanitizing here (an Arabic-only item name and no code): the disambiguator
        // part is DROPPED, so the name never ends in a separator and never carries a word that is not the
        // lot's identity.
        var data = RawLot(withMeter: true);
        data.Rows[0].ItemCode = string.Empty;
        data.Rows[0].ItemName = "قطن";
        var (controller, _) = BuildController(data);
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));

        var file = (FileContentResult)await controller.ExportLotCard(
            StockScopes.Raw, LotId, null, null, null, null, null, export);

        file.FileDownloadName.Should().Be("lot-card-MSG-2026-000007.pdf");
    }

    // ------------------------------------------------------- the permission

    [Fact]
    public void TheCardFile_CarriesTheSamePermissionAsTheTraceabilityScreen()
    {
        var action = typeof(InventoryTraceabilityController)
            .GetMethod(nameof(InventoryTraceabilityController.ExportLotCard))!;

        var policy = action.GetCustomAttribute<AuthorizeAttribute>(inherit: true)!.Policy;

        policy.Should().Be(PermissionPolicy.Prefix + Permissions.InventoryView,
            "a user who may not see the lot's data may not download it either");
    }

    [Fact]
    public async Task WithoutTheInventoryViewPermission_ThePolicyRefusesTheDownload()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        var authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        static ClaimsPrincipal Principal(params string[] permissions) =>
            new(new ClaimsIdentity(permissions.Select(p => new Claim(ClaimTypes.Role, p)), authenticationType: "TestAuth"));

        var policyName = PermissionPolicy.Prefix + Permissions.InventoryView;

        (await authorization.AuthorizeAsync(Principal(Permissions.InventoryView), null, policyName))
            .Succeeded.Should().BeTrue("holding inventory.view is enough - no admin role is required");

        (await authorization.AuthorizeAsync(Principal(Permissions.CustomersView), null, policyName))
            .Succeeded.Should().BeFalse("holding an unrelated right never satisfies inventory.view");

        (await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policyName))
            .Succeeded.Should().BeFalse("a signed-out caller never reaches the lot's data");
    }
}
