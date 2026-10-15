using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Common;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The item list's downloadable files, driven through the REAL controller and the REAL renderer.
///
/// Two defects are covered here, and each assertion is about the byte the operator ends up with
/// rather than about a call being made:
///
///   1. `/items/export` defaults to `excel`, so a client that asked for the printable list without
///      saying `format=pdf` received REAL xlsx bytes under an `items.pdf` name. The PDF request must
///      therefore produce an actual PDF, and the Excel request must keep producing an actual workbook.
///   2. The screen's file endpoints are permission-guarded (`items.view` for the import template,
///      `reports.export` for the exports). The UI may only offer a control whose endpoint the caller
///      could actually use - and the server check must remain in place behind it, not be replaced by it.
/// </summary>
public class ItemsExportFormatTests : Phase8TestBase
{
    private const string CompanyAr = "مصنع الصباغة والتجهيز";
    private const string CompanyEn = "Dye & Finishing Factory";

    private static ReportExportService ExportService() =>
        new(new TestCompanyIdentityProvider(CompanyAr, CompanyEn));

    private static ItemsController Controller(ApplicationDbContext db) => new(Sender(db));

    private static MemoryStream AsStream(FileContentResult file) => new(file.FileContents);

    [Fact]
    public async Task ThePdfExport_IsARealPdf_NotASpreadsheetRenamed()
    {
        using var db = NewDb();
        db.Items.Add(new Item("IT-1", "Fabric", UnitOfMeasure.KG, "tester", nameAr: "قماش"));
        await db.SaveChangesAsync();

        var result = await Controller(db).Export(activeOnly: null, search: null, format: "pdf", export: ExportService());

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be("items.pdf");

        // The PDF magic number. An .xlsx is a zip instead and starts with "PK" - which is exactly the
        // mix-up this guards against, so the first byte is checked both ways round.
        Encoding.ASCII.GetString(file.FileContents, 0, 5).Should().Be("%PDF-");
        file.FileContents[0].Should().NotBe(0x50, "the file must not be a workbook renamed to .pdf");
    }

    [Fact]
    public async Task TheExcelExport_IsUnchanged_ForTheDefault_AnExplicitExcelRequest_AndTheDedicatedEndpoint()
    {
        using var db = NewDb();
        db.Items.Add(new Item("IT-1", "Fabric", UnitOfMeasure.KG, "tester", nameAr: "قماش"));
        await db.SaveChangesAsync();
        var controller = Controller(db);

        // No `format` at all (the endpoint's own default), an explicit excel request, and the
        // standalone /export/excel action the screen's Excel button calls.
        var results = new IActionResult[]
        {
            await controller.Export(activeOnly: null, search: null, export: ExportService()),
            await controller.Export(activeOnly: null, search: null, format: "excel", export: ExportService()),
            await controller.ExportExcel(activeOnly: null, search: null, export: ExportService())
        };

        foreach (var result in results)
        {
            var file = result.Should().BeOfType<FileContentResult>().Subject;
            file.ContentType.Should().Be(ExportFileHelper.ExcelContentType);
            file.FileDownloadName.Should().Be("items.xlsx");

            file.FileContents[0].Should().Be(0x50, "an .xlsx is a zip archive");
            file.FileContents[1].Should().Be(0x4B);

            // A workbook only the OpenXML format itself can open: the exported columns and the row
            // data are still what the report contract promises.
            using var workbook = new XLWorkbook(AsStream(file));
            var sheet = workbook.Worksheets.First();
            sheet.Cell(1, 1).GetString().Should().Be("Code");
            sheet.Cell(1, 8).GetString().Should().Be("Active");
            sheet.Cell(2, 1).GetString().Should().Be("IT-1");
            sheet.Cell(2, 2).GetString().Should().Be("قماش");
        }
    }

    [Fact]
    public void TheImportTemplate_IsStillARealWorkbook()
    {
        using var db = NewDb();

        var file = Controller(db).ImportTemplate(ExportService())
            .Should().BeOfType<FileContentResult>().Subject;

        file.ContentType.Should().Be(ExportFileHelper.ExcelContentType);
        file.FileDownloadName.Should().Be("items-import-template.xlsx");

        using var workbook = new XLWorkbook(AsStream(file));
        var headers = workbook.Worksheets.First().Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headers.Should().Equal(ItemImportTemplate.Headers);
    }

    [Theory]
    [InlineData(nameof(ItemsController.Export), Permissions.ReportsExport)]
    [InlineData(nameof(ItemsController.ExportExcel), Permissions.ReportsExport)]
    [InlineData(nameof(ItemsController.ImportTemplate), Permissions.ItemsView)]
    [InlineData(nameof(ItemsController.PreviewImport), Permissions.ItemsCreate)]
    [InlineData(nameof(ItemsController.ExecuteImport), Permissions.ItemsCreate)]
    [InlineData(nameof(ItemsController.ExecuteImportWithUpdate), Permissions.ItemsEdit)]
    public void EveryItemsFileEndpoint_KeepsItsGranularPermissionPolicy(string action, string permission)
    {
        var method = typeof(ItemsController)
            .GetMethod(action, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ItemsController.{action} not found");

        var policies = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .Where(p => p is not null)
            .ToList();

        policies.Should().Contain(PermissionPolicy.Prefix + permission,
            because: $"ItemsController.{action} must stay controlled by the configurable '{permission}' right");
    }

    /// <summary>
    /// The screen and the API must agree, and neither may invent a permission name:
    ///   * the PDF action asks for `format=pdf` explicitly, because `/items/export` defaults to Excel;
    ///   * the export buttons and the template button are only rendered for a caller holding the
    ///     permission their endpoint checks, and those names are the API's own constants.
    /// </summary>
    [Fact]
    public void TheItemsScreen_AsksForPdfExplicitly_AndGatesEachControlBehindThePermissionItsEndpointRequires()
    {
        var exports = File.ReadAllText(RepoFile(Path.Combine("src", "DyeHouseERP.Web", "src", "api", "exports.ts")));

        Regex.IsMatch(exports, @"pdf:\s*\(\)\s*=>\s*downloadFile\(""/items/export"",\s*""items\.pdf"",\s*\{\s*format:\s*""pdf""\s*\}\)")
            .Should().BeTrue("ItemsExports.pdf must request format=pdf instead of taking the endpoint's excel default");

        var page = File.ReadAllText(RepoFile(Path.Combine("src", "DyeHouseERP.Web", "src", "pages", "ItemsPage.tsx")));

        Regex.IsMatch(page, @"canExportList\s*&&\s*\(?\s*<ExportButtons")
            .Should().BeTrue("the list exports must not be offered without reports.export");
        Regex.IsMatch(page, @"canDownloadTemplate\s*&&[\s\S]{0,200}?ItemsExports\.template\(\)")
            .Should().BeTrue("the import template must not be offered without items.view");

        // Every permission the screen reads has to be one the backend actually defines - the set is
        // exactly the two rights this screen's file endpoints demand.
        var used = Regex.Matches(page, @"actor\.can\(""([^""]+)""\)").Select(m => m.Groups[1].Value).ToList();
        used.Should().BeEquivalentTo(new[] { Permissions.ItemsView, Permissions.ReportsExport });
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"'{relative}' is not part of this checkout.");
    }
}
