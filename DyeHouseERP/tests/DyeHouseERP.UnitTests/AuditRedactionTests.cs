using System.Reflection;
using System.Text;
using DyeHouseERP.Infrastructure.Services;
using DyeHouseERP.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// B2 redaction rules: no secret value and no binary content may ever reach
/// an audit payload, regardless of which entity carries it.
/// </summary>
public class AuditRedactionTests
{
    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("passwordhash")]            // case-insensitive
    [InlineData("NewPasswordHash")]          // suffix match covers variants
    [InlineData("AccessToken")]              // "...Token" suffix
    [InlineData("ClientSecret")]
    [InlineData("ApiKey")]
    public void SensitiveNames_AreFlagged(string propertyName)
        => AuditValueRedactor.IsSensitiveName(propertyName).Should().BeTrue();

    [Theory]
    [InlineData("Username")]
    [InlineData("ItemCode")]
    [InlineData("TokenBalance")]             // "Token" inside, not suffix
    [InlineData("Notes")]
    public void OrdinaryNames_AreNotFlagged(string propertyName)
        => AuditValueRedactor.IsSensitiveName(propertyName).Should().BeFalse();

    [Fact]
    public void PasswordHash_Value_IsNeverReturned()
    {
        var redacted = AuditValueRedactor.RedactIfSensitive("PasswordHash", "PBKDF2$100000$salt$realhash");
        redacted.Should().Be(AuditValueRedactor.RedactedMarker);
        redacted!.ToString().Should().NotContain("PBKDF2").And.NotContain("realhash");
    }

    [Fact]
    public void AttachmentContent_Bytes_AreNeverReturned_OnlySizeMarker()
    {
        var content = Encoding.UTF8.GetBytes(new string('x', 25 * 1024 * 1024)); // a 25 MB upload
        var redacted = AuditValueRedactor.RedactIfSensitive("Content", content);

        redacted.Should().Be("[REDACTED size 26214400]");
        ((string)redacted!).Length.Should().BeLessThan(30);
    }

    [Fact]
    public void AnyByteArray_IsRedacted_RegardlessOfPropertyName()
    {
        var redacted = AuditValueRedactor.RedactIfSensitive("SomeFutureBinaryColumn", new byte[] { 1, 2, 3 });
        redacted.Should().Be("[REDACTED size 3]");
    }

    [Fact]
    public void OrdinaryValues_PassThrough_Unchanged()
    {
        AuditValueRedactor.RedactIfSensitive("Username", "admin").Should().Be("admin");
        AuditValueRedactor.RedactIfSensitive("QuantityKg", 12.5m).Should().Be(12.5m);
        AuditValueRedactor.RedactIfSensitive("Notes", (string?)null).Should().BeNull();
    }
}

/// <summary>
/// B1: a missing or blank Jwt:Key must fail fast - never fall back to a
/// hardcoded secret.
/// </summary>
public class JwtKeyValidationTests
{
    private static JwtTokenService ServiceWithKey(string? key) => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "DyeHouseERP",
                ["Jwt:Audience"] = "DyeHouseERP.Clients",
                ["Jwt:Key"] = key
            })
            .Build());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankKey_Throws_ClearConfigurationError(string? key)
    {
        var act = () => ServiceWithKey(key).GenerateToken(Guid.NewGuid(), "admin", new[] { "admin" }, out _);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void ConfiguredKey_SignsToken_ThatValidatesWithTheSameKey()
    {
        var key = "unit-test-signing-key-0123456789abcdef0123456789abcdef";
        var service = ServiceWithKey(key);

        var token = service.GenerateToken(Guid.NewGuid(), "admin", new[] { "admin" }, out var expiresAt);

        token.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeAfter(DateTime.UtcNow);
        // Symmetric proof: the token's signature verifies against the very key
        // read from configuration (the same value Program.cs validation uses).
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var validationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "DyeHouseERP",
            ValidAudience = "DyeHouseERP.Clients",
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
        handler.ValidateToken(token, validationParameters, out _);
    }

    [Fact]
    public void NoHardcodedFallback_ExistsInJwtTokenService()
    {
        // Guard against regression: the service source must not contain any
        // literal fallback key assignment ("?? \"...\"") for the JWT key.
        var source = File.ReadAllText(FindSourceFile("JwtTokenService.cs"));
        source.Should().NotContain("?? \"", because: "a null-coalescing string literal would be a hardcoded fallback secret");
        source.Should().NotContain("dev-only");
    }

    private static string FindSourceFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DyeHouseERP.sln")))
            dir = dir.Parent!;
        var candidate = Directory.EnumerateFiles(dir!.FullName, fileName, SearchOption.AllDirectories)
            .First(f => !f.Contains(Path.Combine("bin")) && !f.Contains(Path.Combine("obj")));
        return candidate;
    }
}
