using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.DTOs;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The printable permit's QUANTITY columns (spec sections 31 and 39): KG, Meter and the roll count.
///
/// A sub-customer who receives goods by the metre only must not have an empty KG column on their
/// paper, so the three columns are choosable - and the choice has to reach BOTH surfaces that produce
/// the document: the in-app print preview (a React page) and the server-generated PDF the register
/// downloads. This suite covers the PDF half, which is the part reachable without a browser: it drives
/// the real controller action and captures exactly what it hands to the PDF renderer.
///
/// The rules the assertions pin, in the brief's own terms:
///   * the choice affects the PRINT-OUT only - the identifying columns (job order, item, colour,
///     drawing number) always print, and no quantity is re-derived, converted or dropped from the data;
///   * a hidden column is left out of BOTH the header row and every data row, so the table can never
///     drift out of alignment;
///   * the default (nothing chosen) is the complete document.
/// </summary>
public class DeliveryPermitPdfColumnsTests
{
    /// <summary>
    /// Stands in for the real PDF renderer: it produces no bytes, but it records the exact headers and
    /// rows the controller asked for, which is what the column choice is visible in.
    /// </summary>
    private sealed class CapturingExportService : IReportExportService
    {
        public IReadOnlyList<string> Headers { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<object?[]> Rows { get; private set; } = Array.Empty<object?[]>();

        public byte[] GeneratePdf(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
        {
            Headers = headers.ToList();
            Rows = rows.Select(r => r.ToArray()).ToList();
            return new byte[] { 1 };
        }

        public byte[] GenerateExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
            => Array.Empty<byte>();
    }

    private static DeliveryDto Permit() => new()
    {
        Id = Guid.NewGuid(),
        DeliveryNumber = "SUB-2026-000041",
        DeliveryDate = new DateTime(2026, 10, 14),
        CustomerCode = "C-S",
        CustomerName = "Sub Buyer",
        Status = DeliveryStatus.Prepared,
        IsSubCustomerPermit = true,
        PrimaryDeliveryNumber = "DEL-2026-000125",
        Lines = new List<DeliveryLineDto>
        {
            new()
            {
                Id = Guid.NewGuid(), ProductionOrderNumber = "PRD-P-1", ItemCode = "IT-1", Color = "Red",
                QuantityKg = 120m, QuantityMeter = 400m, PieceCount = 7,
                DrawingNumbers = new List<string> { "DRW-9" }
            }
        }
    };

    private static (DeliveriesController Controller, CapturingExportService Export) BuildController(DeliveryDto? permit = null)
    {
        var sender = new Mock<ISender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<GetDeliveryByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permit ?? Permit());

        var export = new CapturingExportService();
        return (new DeliveriesController(sender.Object), export);
    }

    private static async Task<(IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows)> RenderAsync(
        bool includeKg, bool includeMeter, bool includeRolls)
    {
        var (controller, export) = BuildController();
        var result = await controller.GetDeliveryPdf(
            Guid.NewGuid(), includeKg, includeMeter, includeRolls, export);

        result.Should().BeOfType<FileContentResult>($"the permit PDF is a file response: {result.GetType().Name}");
        return (export.Headers, export.Rows);
    }

    [Fact]
    public async Task ByDefault_EveryQuantityColumnPrints()
    {
        var (headers, rows) = await RenderAsync(includeKg: true, includeMeter: true, includeRolls: true);

        headers.Should().ContainInOrder("Qty KG", "Qty Meter", "Pieces");
        rows.Single().Should().ContainInOrder(120m, 400m, 7);
    }

    [Fact]
    public async Task AMetreOnlyDocument_PrintsNoKgColumn_AndStillPrintsItsIdentity()
    {
        // The exact case the brief describes: the sub-customer receives metres, so the KG column is
        // switched off - while the document still identifies itself (job order, item, colour, drawing).
        var (headers, rows) = await RenderAsync(includeKg: false, includeMeter: true, includeRolls: false);

        headers.Should().NotContain("Qty KG");
        headers.Should().NotContain("Pieces");
        headers.Should().Contain(new[] { "Production Order", "Item", "Color", "Qty Meter", "Drawing number / رقم الرسمة" });

        // One cell per header, in the same order: the hidden columns are absent from the row too, so the
        // table cannot shift out of alignment.
        rows.Single().Should().HaveCount(headers.Count);
        rows.Single().Should().ContainInOrder("PRD-P-1", "IT-1", "Red", 400m, "DRW-9");
        rows.Single().Should().NotContain(120m, "the KG quantity is hidden on paper, not invented or zeroed");
    }

    [Fact]
    public async Task HidingAColumn_ChangesNoRecordedQuantity()
    {
        // Printing is a projection: the same permit rendered with every column, and with the KG column
        // hidden, must agree on every figure that IS printed.
        var (wideHeaders, wideRows) = await RenderAsync(true, true, true);
        var (narrowHeaders, narrowRows) = await RenderAsync(includeKg: false, includeMeter: true, includeRolls: true);

        var wide = wideHeaders.Zip(wideRows.Single()).ToDictionary(p => p.First, p => p.Second);
        var narrow = narrowHeaders.Zip(narrowRows.Single()).ToDictionary(p => p.First, p => p.Second);

        narrow.Should().NotContainKey("Qty KG");
        foreach (var (header, value) in narrow)
            wide[header].Should().Be(value, $"'{header}' is the same recorded figure in both print-outs");
    }

    [Fact]
    public async Task AMetreOnlyPermit_RendersARealPdfFromTheNarrowedColumns()
    {
        // The reduced column set has to be a table the REAL renderer can print, not just a list a mock
        // counted - so this one runs the production export service and inspects the actual bytes.
        var (controller, _) = BuildController();
        var export = new ReportExportService(new TestCompanyIdentityProvider("شركة تجريبية", "Test Co"));

        var pdf = (FileContentResult)await controller
            .GetDeliveryPdf(Guid.NewGuid(), includeKg: false, includeMeter: true, includeRolls: false, export: export);

        System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5).Should().Be("%PDF-");
        pdf.FileContents.Length.Should().BeGreaterThan(1000);
        pdf.ContentType.Should().Be("application/pdf");
        pdf.FileDownloadName.Should().Be("delivery-SUB-2026-000041.pdf");
    }

    [Fact]
    public async Task EveryCombination_KeepsHeadersAndCellsInStep()
    {
        // Three switchable columns => eight formats. Each one must stay a well-formed table, and the
        // quantity cells must follow their own columns exactly.
        foreach (var kg in new[] { true, false })
        foreach (var meter in new[] { true, false })
        foreach (var rolls in new[] { true, false })
        {
            var (headers, rows) = await RenderAsync(kg, meter, rolls);
            var row = rows.Single();

            row.Should().HaveCount(headers.Count, $"format kg={kg}, meter={meter}, rolls={rolls} must stay aligned");
            headers.Should().Contain("Drawing number / رقم الرسمة", "identity always prints");
            headers.Should().Contain("Item", "identity always prints");

            headers.Contains("Qty KG").Should().Be(kg);
            headers.Contains("Qty Meter").Should().Be(meter);
            headers.Contains("Pieces").Should().Be(rolls);

            // The figures keep their own values regardless of what else is hidden: nothing is converted
            // between KG and Meter and no column is zero-filled.
            if (kg) row[headers.ToList().IndexOf("Qty KG")].Should().Be(120m);
            if (meter) row[headers.ToList().IndexOf("Qty Meter")].Should().Be(400m);
            if (rolls) row[headers.ToList().IndexOf("Pieces")].Should().Be(7);
        }
    }
}
