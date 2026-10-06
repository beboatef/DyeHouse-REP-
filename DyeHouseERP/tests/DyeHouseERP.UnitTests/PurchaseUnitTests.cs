using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Domain.Exceptions;
using DyeHouseERP.Persistence.Seeding;
using FluentAssertions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// Spec section 44: bilingual custom units, deactivation instead of deletion, and
/// conversion that only exists when the user explicitly configured it.
/// </summary>
public class PurchaseUnitTests
{
    private static PurchaseUnit Create(string code = "CARTON", string ar = "كرتونة", string en = "Carton")
        => new(code, ar, en, "tester");

    [Fact]
    public void Constructor_WithBothNames_Succeeds()
    {
        var unit = Create();

        unit.NameAr.Should().Be("كرتونة");
        unit.NameEn.Should().Be("Carton");
        unit.IsActive.Should().BeTrue();
        unit.ConversionFactor.Should().BeNull("a unit does not convert unless a factor is configured");
        unit.BaseUnitId.Should().BeNull();
    }

    [Fact]
    public void Constructor_NormalisesCodeToUpperCase()
    {
        Create(code: " half_pack ").Code.Should().Be("HALF_PACK");
    }

    [Theory]
    [InlineData("", "Carton")]
    [InlineData("   ", "Carton")]
    [InlineData("كرتونة", "")]
    public void Constructor_WithMissingName_Throws(string nameAr, string nameEn)
    {
        var act = () => new PurchaseUnit("CARTON", nameAr, nameEn, "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_WithMissingCode_Throws()
    {
        var act = () => new PurchaseUnit("  ", "كرتونة", "Carton", "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_WithConversionFactorButNoTargetUnit_Throws()
    {
        var act = () => new PurchaseUnit("CARTON", "كرتونة", "Carton", "tester", conversionFactor: 20m, baseUnitId: null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_WithTargetUnitButNoFactor_Throws()
    {
        var act = () => new PurchaseUnit("CARTON", "كرتونة", "Carton", "tester", conversionFactor: null, baseUnitId: Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WithNonPositiveFactor_Throws(decimal factor)
    {
        var act = () => new PurchaseUnit("CARTON", "كرتونة", "Carton", "tester", conversionFactor: factor, baseUnitId: Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_WithExplicitFactorAndTarget_Succeeds()
    {
        var pieces = Guid.NewGuid();

        var unit = new PurchaseUnit("CARTON", "كرتونة", "Carton", "tester", conversionFactor: 20m, baseUnitId: pieces);

        unit.ConversionFactor.Should().Be(20m);
        unit.BaseUnitId.Should().Be(pieces);
    }

    [Fact]
    public void Update_CannotPointAUnitAtItself()
    {
        var unit = Create();

        var act = () => unit.Update("كرتونة", "Carton", 20m, unit.Id, null, "tester");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_ChangesNamesAndConversion()
    {
        var unit = Create();
        var pieces = Guid.NewGuid();

        unit.Update("كرتونة كبيرة", "Large Carton", 24m, pieces, "bulk", "tester");

        unit.NameAr.Should().Be("كرتونة كبيرة");
        unit.NameEn.Should().Be("Large Carton");
        unit.ConversionFactor.Should().Be(24m);
        unit.BaseUnitId.Should().Be(pieces);
        unit.Notes.Should().Be("bulk");
        unit.ModifiedBy.Should().Be("tester");
    }

    [Fact]
    public void Deactivate_MarksInactiveButKeepsTheRecordUsable()
    {
        var unit = Create();

        unit.Deactivate("tester");

        unit.IsActive.Should().BeFalse();
        // The unit still resolves - this is why deactivation, not deletion, is the lifecycle.
        unit.NameAr.Should().Be("كرتونة");
        unit.Code.Should().Be("CARTON");

        unit.Activate("tester");
        unit.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SeededDefaults_CoverTheNineUnitsRequiredByTheSpec()
    {
        PurchaseUnitSeeder.Defaults.Should().HaveCount(9);

        var arabicNames = PurchaseUnitSeeder.Defaults.Select(d => d.NameAr).ToList();
        arabicNames.Should().BeEquivalentTo(new[]
        {
            "وحدة", "عدد", "قطعة", "كرتونة", "عبوة", "متر", "كيلو", "بوصة", "نصف بوصة"
        });

        // Every default is bilingual, and none of them carries a conversion - a
        // seeded default that auto-converted would be exactly the implicit
        // conversion spec section 44 forbids.
        PurchaseUnitSeeder.Defaults.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.NameAr));
        PurchaseUnitSeeder.Defaults.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.NameEn));

        var seeded = PurchaseUnitSeeder.Defaults
            .Select(d => new PurchaseUnit(d.Code, d.NameAr, d.NameEn, "system", isSystemDefault: true))
            .ToList();
        seeded.Should().OnlyContain(u => u.ConversionFactor == null && u.BaseUnitId == null);
    }
}
