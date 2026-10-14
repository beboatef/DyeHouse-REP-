using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DyeHouseERP.API.Authorization;
using DyeHouseERP.API.Controllers;
using DyeHouseERP.Application.Attachments.Commands;
using DyeHouseERP.Application.Attachments.DTOs;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Deliveries.Queries;
using DyeHouseERP.Application.Invoices.Queries;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.ProductionOrders.Queries;
using DyeHouseERP.Application.RawReceipts.Queries;
using DyeHouseERP.Application.Reports.Queries;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 8 - reports, exports, item template/import, portal removal, attachments and the QR scan view.
///
/// Each test drives the REAL implementation (the real export renderer, the real import handlers, the real
/// attachment validator/handler, the real read queries the scan view calls) and asserts on the produced
/// artefact, not on a stand-in for it: the bytes of a genuine workbook, the rows a genuine preview returns,
/// the exception a genuine lookup throws.
/// </summary>
public abstract class Phase8TestBase
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    protected Phase8TestBase()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase8-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    protected ApplicationDbContext NewDb() => new(_options);

    protected static ISender Sender(ApplicationDbContext db, ICurrentUserService? user = null)
    {
        user ??= new StubCurrentUser("tester");
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PreviewItemImportCommand>());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(user);
        services.AddSingleton<ICurrentUserService>(user);
        services.AddSingleton<IDateTime>(new FixedClock());
        services.AddSingleton<IPeriodCloseService>(new OpenPeriod());
        services.AddSingleton<IAllocationLockService>(new RecordingStockLock());
        services.AddSingleton<IDocumentNumberGenerator>(new FixedNumberGenerator());
        services.AddSingleton<IMaterialLedgerService>(new MaterialLedgerService(db));
        services.AddSingleton<IInventoryLedgerService>(new InventoryLedgerService(db));
        services.AddSingleton<IExcelImportReader>(new ExcelImportReader());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    protected static byte[] BuildItemWorkbook(params object?[][] dataRows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Items");
        for (var c = 0; c < ItemImportTemplate.Headers.Length; c++)
            sheet.Cell(1, c + 1).Value = ItemImportTemplate.Headers[c];
        for (var r = 0; r < dataRows.Length; r++)
            for (var c = 0; c < dataRows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = dataRows[r][c]?.ToString() ?? string.Empty;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Concatenates every part of the .xlsx (a zip) - the only way to see the print header/footer.</summary>
    protected static string ReadAllWorkbookText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var text = new System.Text.StringBuilder();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            text.Append(reader.ReadToEnd());
        }
        return text.ToString();
    }
}

// ============================================================================================
//  1. Exports: real content (Arabic, units, identity), and an export moves no stock or money
// ============================================================================================

public class Phase8ExportTests : Phase8TestBase
{
    private const string CompanyAr = "مصنع الغزل والأصباغة";
    private const string CompanyEn = "Textile & Dye Factory";

    private static ReportExportService ExportService() =>
        new(new TestCompanyIdentityProvider(CompanyAr, CompanyEn));

    [Fact]
    public async Task AnExcelExportOfTheInventoryLedger_CarriesArabicData_UnitsAreNeverMixed_AndItMovesNothing()
    {
        using var db = NewDb();

        // Real data behind the report: a customer, an item and one posted ledger row.
        var customer = new Customer("C-P8", "عميل القطن", "seed");
        var item = new Item("ITM-P8", "قطن 100", UnitOfMeasure.KG, "seed", nameAr: "قطن 100", nameEn: "Cotton 100");
        var warehouse = new Warehouse("WH-P8", "المخزن الرئيسي", WarehouseKind.RawMaterial, "seed");
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.PurchaseReceipt, "RCV-P8", Guid.NewGuid(), DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, rawMessageId: null, productionOrderId: null,
            quantityKg: 100m, quantityMeter: null, TransactionDirection.In, "seed"));
        await db.SaveChangesAsync();

        var movements = await Sender(db).Send(new GetInventoryMovementsQuery(), CancellationToken.None);
        movements.Should().ContainSingle("the report is built from the real ledger, never a hard-coded total");

        // The exact header set the controller uses: kilograms and meters are SEPARATE columns.
        var headers = new List<string>
            { "Date", "SourceDocument", "Warehouse", "Customer", "Item", "QuantityKg", "QuantityMeter", "Direction" };
        var rows = movements.Select(m => new object?[]
        {
            m.TransactionDate.ToString("yyyy-MM-dd"), m.SourceDocumentNumber, m.WarehouseName,
            m.CustomerName, m.ItemName, m.QuantityKg, m.QuantityMeter, m.Direction.ToString()
        }).ToList();

        var before = (Inventory: await db.InventoryTransactions.CountAsync(),
                      Material: await db.MaterialTransactions.CountAsync(),
                      Ledger: await db.CustomerLedgerEntries.CountAsync());

        var bytes = ExportService().GenerateExcel("حركات المخزون", headers, rows);

        bytes.Length.Should().BeGreaterThan(0, "a real workbook must be produced");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.RightToLeft.Should().BeTrue("the factory's exports are Arabic, so the sheet is right-to-left");

        var used = sheet.CellsUsed().Select(c => c.GetString()).ToList();
        used.Should().Contain("عميل القطن", "Arabic data from the source record must survive into the file");
        used.Should().Contain("Cotton 100", "the item's canonical name is exported");
        used.Should().Contain("100", "the seeded kilogram quantity is exported");

        // Factory identity comes from the identity provider, never a literal. It is written into the
        // worksheet's print header, so it is only visible in the raw OOXML.
        ReadAllWorkbookText(bytes).Should().Contain(CompanyAr);

        var after = (Inventory: await db.InventoryTransactions.CountAsync(),
                     Material: await db.MaterialTransactions.CountAsync(),
                     Ledger: await db.CustomerLedgerEntries.CountAsync());
        after.Should().Be(before, "generating an export is read-only: it posts no stock movement and no financial entry");
    }

    [Fact]
    public void APdfExport_IsARealPdfFile()
    {
        var bytes = ExportService().GeneratePdf("تقرير تجاوز الرصيد السالب", "2026-10-01 - 2026-10-14",
            new List<string> { "Date", "Item", "Requested" }, new List<object?[]> { new object?[] { "2026-10-05", "قطن", 12m } });

        bytes.Length.Should().BeGreaterThan(0);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF", "a real PDF, not a stub");
    }

    /// <summary>
    /// The PDF carries REAL Arabic text, not boxes and not a picture of text.
    ///
    /// This is the check Phase 4/8/9 left open ("PDF Arabic glyph shaping unverified - deployment font").
    /// Two independent facts are pinned on the produced bytes:
    ///   1. an Arabic-capable font program is EMBEDDED in the file (/FontFile2) under the family the
    ///      renderer registered (Noto Naskh Arabic) - so any host renders it, no system font needed; and
    ///   2. the file's /ToUnicode mappings resolve to Arabic code points - so the text layer holds
    ///      Arabic characters (copyable/searchable), not just glyph indices.
    /// </summary>
    [Fact]
    public void APdfExport_EmbedsTheArabicFont_AndCarriesRealArabicText()
    {
        var bytes = ExportService().GeneratePdf("كشف حساب العميل", null,
            new List<string> { "البيان", "المبلغ" }, new List<object?[]> { new object?[] { "قطن 100", 12m } });

        var latin = System.Text.Encoding.Latin1.GetString(bytes);

        // 1. A TrueType font PROGRAM is embedded (not merely referenced by name).
        latin.Should().Contain("/FontFile2", "the Arabic font must be embedded so any host can render it");

        // 2. The embedded font is the one this project registers (subset prefix + family name).
        latin.Should().Contain("NotoNaskhArabic", "the embedded font is the registered Arabic font");

        // 3. The text layer maps glyphs back to real Arabic code points (base letters or their
        //    Arabic presentation forms), so the Arabic is genuine extractable text.
        var arabicMapped = DecodedPdfStreams(bytes)
            .Where(stream => stream.Contains("beginbfchar") || stream.Contains("beginbfrange"))
            .SelectMany(stream => Regex.Matches(stream, @"<[0-9A-Fa-f]+>\s*<([0-9A-Fa-f]{4,6})>").Cast<Match>())
            .Select(m => Convert.ToInt32(m.Groups[1].Value, 16))
            .Any(cp => (cp >= 0x0600 && cp <= 0x06FF)     // Arabic
                    || (cp >= 0xFB50 && cp <= 0xFDFF)     // Arabic Presentation Forms-A
                    || (cp >= 0xFE70 && cp <= 0xFEFF));   // Arabic Presentation Forms-B

        arabicMapped.Should().BeTrue(
            "the PDF's /ToUnicode must resolve to Arabic code points - that is what makes the Arabic real text");
    }

    /// <summary>
    /// Each PDF stream decoded independently: FlateDecode when it applies, otherwise the raw bytes.
    /// Used to read QuestPDF's /ToUnicode CMaps, which are compressed streams in the file.
    /// </summary>
    private static IEnumerable<string> DecodedPdfStreams(byte[] pdf)
    {
        var latin = System.Text.Encoding.Latin1.GetString(pdf);
        var decoded = new List<string>();
        var cursor = 0;
        while (true)
        {
            var at = latin.IndexOf("stream", cursor, StringComparison.Ordinal);
            if (at < 0) break;
            if (at >= 3 && latin.Substring(at - 3, 3) == "end") { cursor = at + 6; continue; }

            var start = at + "stream".Length;
            if (start < latin.Length && latin[start] == '\r') start++;
            if (start < latin.Length && latin[start] == '\n') start++;
            var end = latin.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) break;

            var raw = new byte[end - start];
            Array.Copy(pdf, start, raw, 0, raw.Length);

            string text;
            try
            {
                using var compressed = new MemoryStream(raw);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var reader = new StreamReader(zlib, System.Text.Encoding.Latin1);
                text = reader.ReadToEnd();
            }
            catch { text = System.Text.Encoding.Latin1.GetString(raw); }

            decoded.Add(text);
            cursor = end + "endstream".Length;
        }
        return decoded;
    }

    [Fact]
    public async Task TheInventoryMovementReport_KeepsKilogramsAndMetersInSeparateFields()
    {
        using var db = NewDb();
        var customer = new Customer("C-P8B", "Customer", "seed");
        var item = new Item("ITM-P8B", "Dual Unit", UnitOfMeasure.KG, "seed", secondaryUnit: UnitOfMeasure.Meter);
        var warehouse = new Warehouse("WH-P8B", "Store", WarehouseKind.RawMaterial, "seed");
        db.Customers.Add(customer);
        db.Items.Add(item);
        db.Warehouses.Add(warehouse);
        db.InventoryTransactions.Add(new InventoryTransaction(
            DocumentType.PurchaseReceipt, "RCV-P8B", Guid.NewGuid(), DateTime.UtcNow,
            warehouse.Id, customer.Id, item.Id, null, null, 250m, 480m, TransactionDirection.In, "seed"));
        await db.SaveChangesAsync();

        var movement = (await Sender(db).Send(new GetInventoryMovementsQuery(), CancellationToken.None)).Single();

        movement.QuantityKg.Should().Be(250m);
        movement.QuantityMeter.Should().Be(480m);
        movement.QuantityKg.Should().NotBe(movement.QuantityMeter, "the two measurements are independent and never converted");
    }
}

// ============================================================================================
//  2. Excel item template + import
// ============================================================================================

public class Phase8ItemImportTests : Phase8TestBase
{
    [Fact]
    public void TheTemplate_HasTheItemModelColumns_AndDownloadsAsANonEmptyWorkbook()
    {
        ItemImportTemplate.Headers.Should().Equal(
            "Code", "NameAr", "NameEn", "Category", "BaseUnit", "SecondaryUnit", "RequireBothUnits");

        var bytes = new ReportExportService(new TestCompanyIdentityProvider("مصنع", "Factory"))
            .GenerateExcel("Items", ItemImportTemplate.Headers, ItemImportTemplate.SampleRows());

        bytes.Length.Should().BeGreaterThan(0, "the downloaded template must not be empty");

        using var stream = new MemoryStream(bytes);
        new ExcelImportReader().ReadRows(stream, out var headers);
        headers.Should().Equal(ItemImportTemplate.Headers);
    }

    [Fact]
    public async Task TheDownloadedTemplate_ReImportsCleanly()
    {
        using var db = NewDb();
        var bytes = new ReportExportService(new TestCompanyIdentityProvider("مصنع", "Factory"))
            .GenerateExcel("Items", ItemImportTemplate.Headers, ItemImportTemplate.SampleRows());

        var preview = await Sender(db).Send(new PreviewItemImportCommand(bytes), CancellationToken.None);

        preview.TotalRows.Should().Be(3);
        preview.InvalidRows.Should().Be(0, "the sample rows in our own template must all be importable");
        preview.ValidRows.Should().Be(3);
        (await db.Items.CountAsync()).Should().Be(0, "a preview never writes");
    }

    [Fact]
    public async Task AnImport_CreatesOnlyValidRows_ReportsTheBadRows_AndIsIdempotentOnRetry()
    {
        using var db = NewDb();
        var bytes = BuildItemWorkbook(
            new object?[] { "ITM-100", "قطن", "Cotton", "Fabric", "KG", "", "No" },
            new object?[] { "ITM-200", "قماش", "Fabric", "Fabric", "Yard", "", "No" }); // invalid unit

        var sender = Sender(db);
        var first = await sender.Send(new ExecuteItemImportCommand(bytes), CancellationToken.None);

        first.Created.Should().Be(1, "only the valid row is created");
        first.Skipped.Should().Be(1);
        first.Errors.Should().ContainSingle(e => e.Contains("ITM-200"), "the rejected row must be reported, never silently dropped");
        (await db.Items.CountAsync()).Should().Be(1, "an invalid row is never imported");

        // Retry the SAME file: the created code now exists, so nothing is created again.
        var retry = await sender.Send(new ExecuteItemImportCommand(bytes), CancellationToken.None);
        retry.Created.Should().Be(0, "a retry is idempotent - no duplicate item");
        (await db.Items.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportingAnExistingCode_IsRefusedWithoutPermission_AndUpdatesWithIt()
    {
        using var db = NewDb();
        db.Items.Add(new Item("ITM-DUP", "Old", UnitOfMeasure.KG, "seed", nameAr: "قديم", nameEn: "Old"));
        await db.SaveChangesAsync();

        var bytes = BuildItemWorkbook(new object?[] { "ITM-DUP", "جديد", "New", "Fabric", "KG", "", "No" });
        var sender = Sender(db);

        var refused = await sender.Send(new ExecuteItemImportCommand(bytes), CancellationToken.None);
        refused.Updated.Should().Be(0);
        refused.Skipped.Should().Be(1, "an existing code is never overwritten automatically");

        var allowed = await sender.Send(new ExecuteItemImportCommand(bytes, AllowExistingUpdate: true), CancellationToken.None);
        allowed.Updated.Should().Be(1);
        (await db.Items.CountAsync()).Should().Be(1, "an update never creates a second row");
        (await db.Items.SingleAsync()).NameEn.Should().Be("New");
    }

    [Fact]
    public async Task ADuplicateCodeInsideOneFile_IsRejected()
    {
        using var db = NewDb();
        var bytes = BuildItemWorkbook(
            new object?[] { "ITM-SAME", "أ", "A", "Fabric", "KG", "", "No" },
            new object?[] { "ITM-SAME", "ب", "B", "Fabric", "KG", "", "No" });

        var result = await Sender(db).Send(new PreviewItemImportCommand(bytes), CancellationToken.None);

        result.InvalidRows.Should().Be(1, "a duplicated code is refused");
        result.Rows.Single(r => !r.IsValid).Errors.Should().Contain(e => e.Contains("duplicated"));
    }
}

// ============================================================================================
//  3. Customer portal removal (a delete: no route, no endpoint, no permission)
// ============================================================================================

public class Phase8PortalRemovalTests
{
    private static IEnumerable<Type> Controllers() =>
        typeof(AttachmentsController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    [Fact]
    public void NoApiController_NoRoute_AndNoPermission_StillReferencesTheCustomerPortal()
    {
        foreach (var controller in Controllers())
        {
            controller.Name.Should().NotContainEquivalentOf("portal");
            foreach (var route in controller.GetCustomAttributes<RouteAttribute>(inherit: true))
                route.Template.Should().NotContainEquivalentOf("portal",
                    $"{controller.Name} must not expose a portal route");

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                action.Name.Should().NotContainEquivalentOf("portal", $"{controller.Name}.{action.Name} is a portal endpoint");
                foreach (var http in action.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                    (http.Template ?? string.Empty).Should().NotContainEquivalentOf("portal");
            }
        }

        Permissions.All.Should().NotContain(p => p.Contains("portal", StringComparison.OrdinalIgnoreCase),
            "portal-only permissions are removed (a delete), not merely unused");
    }

    [Fact]
    public void TheFrontendRouteTable_HasNoPortalRoute()
    {
        // The route table is compiled into the app, so the cleanest proof inside a unit test is the
        // module that owns the routes. Read it from the repository source tree.
        var path = FindRepoFile(Path.Combine("src", "DyeHouseERP.Web", "src", "App.tsx"));
        path.Should().NotBeNull("the web route table must be present in the checkout");
        var source = File.ReadAllText(path!);
        source.Should().NotContainEquivalentOf("portal");
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

// ============================================================================================
//  4. QR / /scan/:type/:id - authorization and invalid-id behaviour
// ============================================================================================

public class Phase8ScanTests : Phase8TestBase
{
    [Fact]
    public async Task EveryScanDocumentType_ReturnsNotFound_ForAnUnknownId_NeverInventedData()
    {
        using var db = NewDb();
        var sender = Sender(db);
        var unknown = Guid.NewGuid();

        Func<Task> order = () => sender.Send(new GetProductionOrderByIdQuery(unknown), CancellationToken.None);
        Func<Task> invoice = () => sender.Send(new GetInvoiceByIdQuery(unknown), CancellationToken.None);
        Func<Task> delivery = () => sender.Send(new GetDeliveryByIdQuery(unknown), CancellationToken.None);
        Func<Task> raw = () => sender.Send(new GetRawMessageByIdQuery(unknown), CancellationToken.None);

        await order.Should().ThrowAsync<NotFoundException>();
        await invoice.Should().ThrowAsync<NotFoundException>();
        await delivery.Should().ThrowAsync<NotFoundException>();
        await raw.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void EveryScanDocumentType_IsBehindAPermissionPolicy()
    {
        // The four types the /scan/:type/:id view resolves. Each one's {id} GET action must carry a
        // granular policy, so scanning never leaks a record the signed-in user may not see.
        var expected = new (Type Controller, string Permission)[]
        {
            (typeof(ProductionOrdersController), Permissions.ProductionView),
            (typeof(InvoicesController), Permissions.InvoicesView),
            (typeof(DeliveriesController), Permissions.ReadyView),
            (typeof(RawMessagesController), Permissions.RawReceive)
        };

        foreach (var (controller, permission) in expected)
        {
            var byId = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpGetAttribute>(inherit: true)
                    .Any(a => (a.Template ?? string.Empty).Contains("{id:guid}", StringComparison.Ordinal)))
                .ToList();

            byId.Should().NotBeEmpty($"{controller.Name} exposes a get-by-id action for the scan view");

            var policies = byId
                .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .Select(a => a.Policy)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            policies.Should().Contain(PermissionPolicy.Prefix + permission,
                $"{controller.Name}'s scan action must require its own view permission");
        }
    }
}

// ============================================================================================
//  5. Attachments: type/size validation, association, authorization, no public path
// ============================================================================================

public class Phase8AttachmentTests : Phase8TestBase
{
    private static UploadAttachmentCommand Command(string fileName, string contentType, byte[]? content = null) =>
        new(AttachmentEntityTypes.Customer, Guid.NewGuid(), fileName, contentType, content ?? new byte[] { 1, 2, 3 });

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("photo.PNG", "image/png")]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("notes.txt", "text/plain; charset=utf-8")]
    [InlineData("scan.jpeg", "application/octet-stream")]
    public void AllowedDocumentAndImageTypes_AreAccepted(string fileName, string contentType)
    {
        var result = new UploadAttachmentCommandValidator().Validate(Command(fileName, contentType));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("page.html", "text/html")]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("script.js", "application/javascript")]
    [InlineData("payload.xml", "application/xml")]
    [InlineData("run.exe", "application/octet-stream")]
    [InlineData("noextension", "application/pdf")]
    public void ActiveAndUnknownTypes_AreRefused(string fileName, string contentType)
    {
        // F.8: the bytes are served back from the API origin, so an HTML/SVG/script file would be
        // executed by the browser. An allowlist is the only safe gate.
        new UploadAttachmentCommandValidator().Validate(Command(fileName, contentType)).IsValid
            .Should().BeFalse($"{fileName} ({contentType}) is never a safe attachment");
    }

    [Fact]
    public void AnAttachmentLargerThanTheLimit_IsRefused()
    {
        var justOver = new byte[UploadAttachmentCommandValidator.MaxBytes + 1];
        new UploadAttachmentCommandValidator().Validate(Command("big.pdf", "application/pdf", justOver))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task AnAttachment_MustBelongToAKnownRecord_AndNeverBecomesOrphaned()
    {
        using var db = NewDb();
        var sender = Sender(db);

        Func<Task> unknownType = () => sender.Send(
            new UploadAttachmentCommand("NotARealType", Guid.NewGuid(), "a.pdf", "application/pdf", new byte[] { 1 }),
            CancellationToken.None);
        await unknownType.Should().ThrowAsync<NotFoundException>();

        Func<Task> missingOwner = () => sender.Send(
            new UploadAttachmentCommand(AttachmentEntityTypes.Customer, Guid.NewGuid(), "a.pdf", "application/pdf", new byte[] { 1 }),
            CancellationToken.None);
        await missingOwner.Should().ThrowAsync<NotFoundException>("a mistyped id must never leave an orphan attachment");

        (await db.Attachments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AValidAttachment_IsStoredAgainstItsRecord_AndOnlyServedThroughTheAuthorizedController()
    {
        using var db = NewDb();
        var customer = new Customer("C-ATT", "Customer", "seed");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var dto = await Sender(db).Send(
            new UploadAttachmentCommand(AttachmentEntityTypes.Customer, customer.Id, "invoice.pdf", "application/pdf", new byte[] { 9, 8, 7 }),
            CancellationToken.None);

        dto.EntityType.Should().Be(AttachmentEntityTypes.Customer);
        dto.EntityId.Should().Be(customer.Id);
        dto.ContentType.Should().Be("application/pdf");
        (await db.Attachments.CountAsync()).Should().Be(1);

        // The download action is permission-protected: there is no public static path to attachment bytes.
        var download = typeof(AttachmentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.GetCustomAttributes<HttpGetAttribute>(inherit: true)
                .Any(a => (a.Template ?? string.Empty).Contains("content", StringComparison.Ordinal)));

        download.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy)
            .Should().Contain(PermissionPolicy.Prefix + Permissions.AttachmentsView);
    }
}
