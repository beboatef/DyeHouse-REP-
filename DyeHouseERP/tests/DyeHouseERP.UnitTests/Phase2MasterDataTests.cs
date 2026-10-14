using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Customers.Commands;
using DyeHouseERP.Application.Customers.DTOs;
using DyeHouseERP.Application.Items.Commands;
using DyeHouseERP.Application.Items.Queries;
using DyeHouseERP.Application.Settings.Commands;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Enums;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Phase 2 (master data): the explicit item-scoped unit conversion rule, the
/// primary -> sub-customer relationship, and the authoritative company identity consumed by
/// templates/exports. These assert the SERVER-side behaviour, not just domain helpers.
/// </summary>
public class Phase2MasterDataTests
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public Phase2MasterDataTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phase2-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private ApplicationDbContext NewDb() => new(_options);

    private static Item TwoUnitItem(UnitOfMeasure baseUnit = UnitOfMeasure.KG, UnitOfMeasure? secondary = UnitOfMeasure.Meter)
        => new("ITM-1", "Raw fabric", baseUnit, "seed", secondaryUnit: secondary);

    // =====================================================================
    // 1. Explicit, item + operation scoped conversion
    // =====================================================================

    [Fact]
    public async Task ConversionRule_IsCreatedForAnItemAndOperation_AndIsLookedUpByOperation()
    {
        using var db = NewDb();
        var item = TwoUnitItem();
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var dto = await new SetItemUnitConversionCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.25m, "Delivery"),
            CancellationToken.None);

        dto.Factor.Should().Be(0.25m);
        dto.Operation.Should().Be("Delivery");

        var rules = await new GetItemUnitConversionsQueryHandler(db).Handle(
            new GetItemUnitConversionsQuery(item.Id, "Delivery"), CancellationToken.None);
        rules.Should().ContainSingle();

        // A different operation has no rule - the conversion is scoped, not global.
        var other = await new GetItemUnitConversionsQueryHandler(db).Handle(
            new GetItemUnitConversionsQuery(item.Id, "Invoice"), CancellationToken.None);
        other.Should().BeEmpty();
    }

    [Fact]
    public async Task ConversionRule_ForTheSameItemAndOperation_IsUpserted_NotDuplicated()
    {
        using var db = NewDb();
        var item = TwoUnitItem();
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var handler = new SetItemUnitConversionCommandHandler(db, new StubCurrentUser("admin"));
        await handler.Handle(new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.25m, "Delivery"), CancellationToken.None);
        await handler.Handle(new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.30m, "Delivery"), CancellationToken.None);

        var rules = await db.ItemUnitConversions.AsNoTracking().Where(c => c.ItemId == item.Id).ToListAsync();
        rules.Should().ContainSingle();
        rules[0].Factor.Should().Be(0.30m);
    }

    [Fact]
    public async Task ConversionRule_IsRefused_WhenTheItemUsesOnlyOneUnit()
    {
        using var db = NewDb();
        var item = new Item("KGONLY", "KG only", UnitOfMeasure.KG, "seed");
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // A single-unit item has nothing to convert, whichever direction is attempted.
        var act = () => new SetItemUnitConversionCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.KG, UnitOfMeasure.Meter, 4m, "Delivery"),
            CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*single unit*");

        var reverse = () => new SetItemUnitConversionCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.25m, "Delivery"),
            CancellationToken.None);
        (await reverse.Should().ThrowAsync<DomainException>()).WithMessage("*single unit*");

        (await db.ItemUnitConversions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void ConversionRule_OnTheEntity_RefusesSameUnitsAndNonPositiveFactor()
    {
        var sameUnits = () => new ItemUnitConversion(Guid.NewGuid(), UnitOfMeasure.KG, UnitOfMeasure.KG, 1m, "Delivery", "seed");
        sameUnits.Should().Throw<DomainException>();

        var badFactor = () => new ItemUnitConversion(Guid.NewGuid(), UnitOfMeasure.KG, UnitOfMeasure.Meter, 0m, "Delivery", "seed");
        badFactor.Should().Throw<DomainException>();

        var noOperation = () => new ItemUnitConversion(Guid.NewGuid(), UnitOfMeasure.KG, UnitOfMeasure.Meter, 2m, "  ", "seed");
        noOperation.Should().Throw<DomainException>();
    }

    [Fact]
    public void Conversion_IsApplied_OnlyWhenARuleForThatOperationExists()
    {
        var rule = new ItemUnitConversion(Guid.NewGuid(), UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.25m, "Delivery", "seed");

        rule.Convert(100m).Should().Be(25m);
        rule.AppliesTo("Delivery", UnitOfMeasure.Meter, UnitOfMeasure.KG).Should().BeTrue();

        // Same item, a different operation: the rule does NOT apply, so nothing converts.
        rule.AppliesTo("Invoice", UnitOfMeasure.Meter, UnitOfMeasure.KG).Should().BeFalse();
        rule.AppliesTo("Delivery", UnitOfMeasure.KG, UnitOfMeasure.Meter).Should().BeFalse();
    }

    [Fact]
    public async Task WithoutAnyRule_ANonConfiguredUnitIsStillRefused_SoThereIsNoGlobalConversion()
    {
        using var db = NewDb();
        var item = new Item("KGONLY", "KG only", UnitOfMeasure.KG, "seed");
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // No conversion rule exists for this item (and none can, being single-unit). A Meter
        // quantity is therefore refused outright - it is never converted from a global factor.
        var act = () => ItemUnitRules.EnsureValidQuantities(
            item.BaseUnit, item.SecondaryUnit, item.RequireBothUnits, null, 1800m, item.Code);

        (act.Should().Throw<DomainException>()).WithMessage("*not configured to use Meter*");
        (await db.ItemUnitConversions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemovingAConversionRule_LeavesNoRuleBehind()
    {
        using var db = NewDb();
        var item = TwoUnitItem();
        db.Items.Add(item);
        await db.SaveChangesAsync();

        await new SetItemUnitConversionCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetItemUnitConversionCommand(item.Id, UnitOfMeasure.Meter, UnitOfMeasure.KG, 0.25m, "Delivery"),
            CancellationToken.None);

        await new RemoveItemUnitConversionCommandHandler(db).Handle(
            new RemoveItemUnitConversionCommand(item.Id, "Delivery"), CancellationToken.None);

        (await db.ItemUnitConversions.CountAsync()).Should().Be(0);
    }

    // =====================================================================
    // 2. Primary -> sub-customer relationship
    // =====================================================================

    private static Customer Customer(string code) => new(code, $"Customer {code}", "seed");

    [Fact]
    public async Task SubCustomer_CanBeAssignedToAParent_AndCleared()
    {
        using var db = NewDb();
        var parent = Customer("C-1");
        var child = Customer("C-2");
        db.AddRange(parent, child);
        await db.SaveChangesAsync();

        var handler = new SetCustomerParentCommandHandler(db, new StubCurrentUser("admin"));
        var dto = await handler.Handle(new SetCustomerParentCommand(child.Id, parent.Id), CancellationToken.None);

        dto.ParentCustomerId.Should().Be(parent.Id);
        dto.ParentCustomerCode.Should().Be("C-1");
        dto.IsSubCustomer.Should().BeTrue();

        var cleared = await handler.Handle(new SetCustomerParentCommand(child.Id, null), CancellationToken.None);
        cleared.ParentCustomerId.Should().BeNull();
        cleared.IsSubCustomer.Should().BeFalse();
    }

    [Fact]
    public async Task SubCustomer_CannotBeItsOwnParent()
    {
        using var db = NewDb();
        var c = Customer("C-1");
        db.Add(c);
        await db.SaveChangesAsync();

        var act = () => new SetCustomerParentCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetCustomerParentCommand(c.Id, c.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*own parent*");
        var reloaded = await db.Customers.AsNoTracking().SingleAsync();
        reloaded.ParentCustomerId.Should().BeNull();
    }

    [Fact]
    public async Task SubCustomer_CannotCreateACycle()
    {
        using var db = NewDb();
        var a = Customer("A");
        var b = Customer("B");
        var c = Customer("C");
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        var handler = new SetCustomerParentCommandHandler(db, new StubCurrentUser("admin"));
        await handler.Handle(new SetCustomerParentCommand(b.Id, a.Id), CancellationToken.None); // B -> A
        await handler.Handle(new SetCustomerParentCommand(c.Id, b.Id), CancellationToken.None); // C -> B -> A

        // Making A a child of C would close A -> B -> C -> A.
        var act = () => handler.Handle(new SetCustomerParentCommand(a.Id, c.Id), CancellationToken.None);
        (await act.Should().ThrowAsync<DomainException>()).WithMessage("*circular*");

        var reloadedA = await db.Customers.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        reloadedA.ParentCustomerId.Should().BeNull();
    }

    [Fact]
    public async Task SubCustomer_ParentMustExist()
    {
        using var db = NewDb();
        var child = Customer("C-2");
        db.Add(child);
        await db.SaveChangesAsync();

        var act = () => new SetCustomerParentCommandHandler(db, new StubCurrentUser("admin")).Handle(
            new SetCustomerParentCommand(child.Id, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<DyeHouseERP.Application.Common.Exceptions.NotFoundException>();
    }

    // =====================================================================
    // 3. Factory settings: the single identity source used by exports
    // =====================================================================

    [Fact]
    public async Task CompanyIdentityProvider_ReturnsTheConfiguredName_AndReflectsAnUpdate_WithNoStaleValue()
    {
        using var db = NewDb();
        var provider = new CompanyIdentityProvider(db);

        // No row yet: a safe default, never a throw.
        provider.Get().DisplayName.Should().Be(CompanyIdentityProvider.DefaultName);

        var settings = new CompanySettings("admin");
        settings.Update("شركة الأصباغ", "Dye House Co", null, "admin");
        db.CompanySettings.Add(settings);
        await db.SaveChangesAsync();

        var first = provider.Get();
        first.NameAr.Should().Be("شركة الأصباغ");
        first.DisplayName.Should().Be("Dye House Co");

        // Update in place: the provider re-reads every call, so no stale identity.
        settings.Update("شركة جديدة", "New Dye Co", TestCompanyIdentityProvider.OnePixelPngDataUrl, "admin");
        await db.SaveChangesAsync();

        var second = provider.Get();
        second.NameEn.Should().Be("New Dye Co");
        second.HasLogo.Should().BeTrue();
    }

    [Fact]
    public void CompanyIdentity_DecodesAValidLogo_AndRefusesBrokenOnes()
    {
        var valid = new CompanyIdentity("ar", "en", TestCompanyIdentityProvider.OnePixelPngDataUrl);
        valid.TryGetLogoBytes(out var bytes, out var contentType).Should().BeTrue();
        bytes.Length.Should().BeGreaterThan(0);
        contentType.Should().Be("image/png");

        new CompanyIdentity("ar", "en", null).TryGetLogoBytes(out _, out _).Should().BeFalse();
        new CompanyIdentity("ar", "en", "not a data url").TryGetLogoBytes(out _, out _).Should().BeFalse();
        new CompanyIdentity("ar", "en", "data:image/png;base64,!!!not-base64!!!").TryGetLogoBytes(out _, out _).Should().BeFalse();
        new CompanyIdentity("ar", "en", "data:text/plain;base64,aGk=").TryGetLogoBytes(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Pdf_UsesTheCompanyIdentity_NotAHardCodedName()
    {
        var headers = new[] { "Ref", "Qty" };
        var rows = new List<object?[]> { new object?[] { "A-1", 5m } };

        var pdfAlpha = new ReportExportService(new TestCompanyIdentityProvider("شركة ألفا", "Alpha Co"))
            .GeneratePdf("Statement", null, headers, rows);
        var pdfBeta = new ReportExportService(new TestCompanyIdentityProvider("شركة بيتا", "Beta Co"))
            .GeneratePdf("Statement", null, headers, rows);

        System.Text.Encoding.ASCII.GetString(pdfAlpha, 0, 5).Should().Be("%PDF-");
        pdfAlpha.Length.Should().BeGreaterThan(500);
        // The rendered document depends on the configured identity - proof it is consumed,
        // and that the old hard-coded "DyeHouse ERP" footer is gone.
        pdfAlpha.Should().NotEqual(pdfBeta);
    }

    [Fact]
    public void Pdf_StillGenerates_WhenTheLogoIsMissing()
    {
        var pdf = new ReportExportService(new TestCompanyIdentityProvider("شركة", "No Logo Co"))
            .GeneratePdf("Statement", null, new[] { "Ref" }, new List<object?[]> { new object?[] { "A-1" } });

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Pdf_StillGenerates_WhenTheLogoIsUnreadableOrCorrupt()
    {
        // A malformed data URL must be skipped, never thrown out of the generator.
        var corruptDataUrl = new ReportExportService(
                new TestCompanyIdentityProvider("شركة", "Corrupt Co", "data:image/png;base64,###not-base64###"))
            .GeneratePdf("Statement", null, new[] { "Ref" }, new List<object?[]> { new object?[] { "A-1" } });

        System.Text.Encoding.ASCII.GetString(corruptDataUrl, 0, 5).Should().Be("%PDF-");

        // Valid-looking base64 but not a real image: QuestPDF cannot decode it - generation must
        // still succeed (the try/catch around the image draw swallows the failure).
        var fakeImage = new ReportExportService(
                new TestCompanyIdentityProvider("شركة", "Fake Co", "data:image/png;base64,aGVsbG8="))
            .GeneratePdf("Statement", null, new[] { "Ref" }, new List<object?[]> { new object?[] { "A-1" } });

        System.Text.Encoding.ASCII.GetString(fakeImage, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Pdf_StillGenerates_WithAValidLogo()
    {
        var pdf = new ReportExportService(
                new TestCompanyIdentityProvider("شركة", "Logo Co", TestCompanyIdentityProvider.OnePixelPngDataUrl))
            .GeneratePdf("Statement", null, new[] { "Ref" }, new List<object?[]> { new object?[] { "A-1" } });

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Excel_StillGenerates_AndKeepsTheHeaderRowFirst()
    {
        var bytes = new ReportExportService(new TestCompanyIdentityProvider("شركة", "Excel Co"))
            .GenerateExcel("Items", new[] { "Code", "Name" }, new List<object?[]> { new object?[] { "ITM-1", "Raw" } });

        bytes.Length.Should().BeGreaterThan(0);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        sheet.Cell(1, 1).GetString().Should().Be("Code", "the identity is carried in the page header/footer, not by shifting rows");
        sheet.Cell(2, 1).GetString().Should().Be("ITM-1");
        sheet.RightToLeft.Should().BeTrue();
    }

    [Fact]
    public void LogoUploadValidator_AcceptsOnlyRealImageDataUrls()
    {
        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl(TestCompanyIdentityProvider.OnePixelPngDataUrl).Should().BeTrue();
        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl("data:image/jpeg;base64,aGk=").Should().BeTrue();

        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl("https://example.com/logo.png").Should().BeFalse();
        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl("data:image/svg+xml;base64,aGk=").Should().BeFalse();
        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl("data:application/pdf;base64,aGk=").Should().BeFalse();
        UpdateCompanySettingsCommandValidator.IsValidImageDataUrl("data:image/png;base64,###").Should().BeFalse();
    }
}
