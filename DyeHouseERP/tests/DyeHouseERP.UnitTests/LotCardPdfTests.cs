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
/// Two halves, both reachable without a browser and without SQL Server:
///   * the DOCUMENT the controller asks the renderer for - its columns, its row/header alignment, the
///     cumulative balance being the query's own figure, the chronological order an official ledger is
///     read in, and KG/Meter staying independent;
///   * the FILE that comes out - real bytes from the production renderer, the PDF magic, the content
///     type and a download name derived from the lot number.
///
/// The rules these pin, because getting them wrong is how a printed document lies:
///   * the card is never WINDOWED - only the lot's identity is accepted, never a date or movement-kind
///     filter, so its rows always add up to its own closing balance;
///   * a unit the lot never recorded prints "—", never a zero;
///   * generating the card is READ-ONLY.
/// </summary>
public class LotCardPdfTests
{
    /// <summary>Records the exact headers and rows the controller asked for; produces no bytes itself.</summary>
    private sealed class CapturingExportService : IReportExportService
    {
        public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<object?[]> Rows { get; private set; } = Array.Empty<object?[]>();
        public string? Title { get; private set; }
        public string? Subtitle { get; private set; }

        public byte[] GeneratePdf(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
        {
            Title = title;
            Subtitle = subtitle;
            Headers = headers.ToList();
            Rows = rows.Select(r => r.ToArray()).ToList();
            return new byte[] { 1 };
        }

        public byte[] GenerateExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
            => Array.Empty<byte>();
    }

    private static readonly Guid LotId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static StockTraceabilityDto RawLot(bool withMeter)
    {
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
            Scope = StockScopes.Raw,
            ItemCode = "FAB-1", ItemName = "Cotton", BaseUnit = "KG",
            RawMessageId = LotId, MessageNumber = "MSG-2026-000007", MessageDate = new DateTime(2026, 10, 1),
            MessageStatus = "PartiallyUsed",
            CustomerCode = "C-1", CustomerName = "Customer", WarehouseName = "Raw Material",
            RemainingKg = 400m, RemainingMeter = withMeter ? 820m : null, MovementCount = 2
        });

        // Newest first, exactly as the query returns them: the card has to re-order them chronologically.
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, Date = new DateTime(2026, 10, 9),
            DocumentType = "ProductionOrder", DocumentNumber = "PRD-2026-000003",
            Kind = "Issued", Direction = "Out", ProductionOrderId = OrderId, OrderNumber = "PRD-2026-000003",
            QuantityKg = 600m, QuantityMeter = withMeter ? 1200m : null, Unit = "KG / Meter",
            BalanceAfterKg = 400m, BalanceAfterMeter = withMeter ? 820m : null,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 9, 8, 0, 0)
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Raw, Date = new DateTime(2026, 10, 1),
            DocumentType = "RawReceiptMessage", DocumentNumber = "MSG-2026-000007",
            Kind = "Received", Direction = "In", RawMessageId = LotId, MessageNumber = "MSG-2026-000007",
            QuantityKg = 1000m, QuantityMeter = withMeter ? 2020m : null, Unit = "KG / Meter",
            BalanceAfterKg = 1000m, BalanceAfterMeter = withMeter ? 2020m : null,
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
            Scope = StockScopes.Materials, MaterialId = LotId, MaterialCode = "MAT-1", MaterialName = "Dye",
            MaterialUnit = "KG", MaterialKind = "Chemical", WarehouseName = "Chemicals", RemainingQuantity = 70m
        });
        dto.Movements.Add(new StockTraceMovementDto
        {
            Id = Guid.NewGuid(), Scope = StockScopes.Materials, Date = new DateTime(2026, 10, 2),
            DocumentType = "SupplyIssue", DocumentNumber = "SUP-2026-000004",
            Kind = "Issued", Direction = "Out", MaterialId = LotId, MaterialCode = "MAT-1",
            Quantity = 30m, Unit = "KG", BalanceAfter = 70m,
            CreatedBy = "operator", CreatedAtUtc = new DateTime(2026, 10, 2, 9, 0, 0)
        });
        return dto;
    }

    /// <summary>
    /// The controller with a stub mediator that returns the prepared read and RECORDS every query it
    /// was asked for - that record is the evidence for "the card is never windowed".
    /// </summary>
    private static (InventoryTraceabilityController Controller, CapturingExportService Export, List<GetStockTraceabilityQuery> Sent)
        BuildController(StockTraceabilityDto data)
    {
        var sent = new List<GetStockTraceabilityQuery>();
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetStockTraceabilityQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<StockTraceabilityDto>, CancellationToken>((query, _) => sent.Add((GetStockTraceabilityQuery)query))
            .ReturnsAsync(data);

        return (new InventoryTraceabilityController(sender.Object), new CapturingExportService(), sent);
    }

    // --------------------------------------------------------------------- the document

    [Fact]
    public async Task TheCard_IsNeverWindowed_OnlyTheLotsIdentityReachesTheQuery()
    {
        var (controller, export, sent) = BuildController(RawLot(withMeter: true));

        await controller.ExportLotCard("raw", LotId, null, null, export);

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
    public async Task EveryRow_KeepsStepWithItsHeaders_IncludingTheCumulativeBalance()
    {
        var (controller, export, _) = BuildController(RawLot(withMeter: true));

        await controller.ExportLotCard("raw", LotId, null, null, export);

        export.Headers.Should().ContainInOrder(
            "التاريخ", "نوع الحركة", "المستند", "الاتجاه", "الكمية (كجم / متر)", "الرصيد التراكمي", "المستخدم");

        export.Rows.Should().HaveCount(2);
        foreach (var row in export.Rows)
            row.Should().HaveCount(export.Headers.Count, "a card with a misaligned table is not a document");

        // Chronological (oldest first): the cumulative column has to accumulate down the sheet.
        export.Rows[0][0].Should().Be("2026-10-01");
        export.Rows[0][1].Should().Be("استلام");
        export.Rows[0][3].Should().Be("وارد");
        export.Rows[0][5].Should().Be("1,000 / 2,020", "the balance after the receipt is the query's own figure");

        export.Rows[1][0].Should().Be("2026-10-09");
        export.Rows[1][1].Should().Be("صرف للتشغيل");
        export.Rows[1][3].Should().Be("صادر");
        export.Rows[1][5].Should().Be("400 / 820", "the closing row's balance is the lot's live balance");
    }

    [Fact]
    public async Task ALotThatNeverRecordedMeter_PrintsNoMeterFigure()
    {
        var (controller, export, _) = BuildController(RawLot(withMeter: false));

        await controller.ExportLotCard("raw", LotId, null, null, export);

        export.Rows.SelectMany(r => r.Where(c => c is string).Cast<string>())
            .Should().NotContain(v => v.Contains('/'), "with no meter recorded the cell is a single KG figure, never half-empty");

        export.Rows[0][4].Should().Be("1,000");
        export.Rows[0][5].Should().Be("1,000");
    }

    [Fact]
    public async Task AMaterialsLot_PrintsItsQuantityInTheMaterialsOwnUnit()
    {
        var (controller, export, sent) = BuildController(MaterialsLot());

        await controller.ExportLotCard("materials", null, null, LotId, export);

        sent.Single().MaterialId.Should().Be(LotId);
        export.Headers.Should().ContainInOrder("الكمية", "الرصيد التراكمي");
        export.Rows.Single()[4].Should().Be("30 KG", "the material's own unit is carried, never forced to KG/Meter");
        export.Rows.Single()[5].Should().Be("70");
    }

    [Fact]
    public async Task AnUnknownOrUnreachableLot_IsANotFound_NotAnEmptyDocument()
    {
        var (controller, export, _) = BuildController(new StockTraceabilityDto { Scope = StockScopes.Raw });

        var result = await controller.ExportLotCard("raw", LotId, null, null, export);

        result.Should().BeOfType<NotFoundObjectResult>("an empty card would look like a real lot with no movements");
    }

    // --------------------------------------------------------------------- the file

    [Fact]
    public async Task TheDownload_IsARealPdf_CarryingTheLotsNumberInItsName()
    {
        var (controller, _, _) = BuildController(RawLot(withMeter: true));
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة تجريبية", "Test Co"));

        var file = (FileContentResult)await controller.ExportLotCard("raw", LotId, null, null, export);

        Encoding.ASCII.GetString(file.FileContents, 0, 5).Should().Be("%PDF-",
            "the response is a genuine PDF, never HTML or JSON wearing a .pdf name");
        file.FileContents.Length.Should().BeGreaterThan(1000);
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be("lot-card-MSG-2026-000007.pdf");
    }

    [Fact]
    public async Task ALotNumberWithUnsafeCharacters_StillYieldsASafeFileName()
    {
        var data = RawLot(withMeter: true);
        data.Rows[0].MessageNumber = "MSG/2026/000007\u0000";
        var (controller, _, _) = BuildController(data);
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Co"));

        var file = (FileContentResult)await controller.ExportLotCard("raw", LotId, null, null, export);

        file.FileDownloadName.Should().Be("lot-card-MSG-2026-000007.pdf",
            "a path separator or control character can never reach the download name");
        file.FileDownloadName.Should().NotContain("/").And.NotContain("\\");
    }

    // --------------------------------------------------------------------- the permission

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
