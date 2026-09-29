namespace DyeHouseERP.Infrastructure.Services;

/// <summary>
/// H2: single, shared validation for the JWT signing key. Used by Program.cs
/// (token validation setup) and JwtTokenService (token issuance) so both
/// always see exactly the same rules.
///
/// B1 established that there is no fallback secret; this closes the gap that
/// remained: appsettings.json shipped a placeholder key ("CHANGE_ME...") which
/// passed the old IsNullOrWhiteSpace check, so an unconfigured production
/// deployment would have started and signed tokens with a secret that is
/// publicly known from the repository.
///
/// There is deliberately no exception, no generated default and no fallback: a
/// deployment must provide its own strong key via user secrets
/// (dotnet user-secrets set "Jwt:Key" "...") or an environment variable
/// (Jwt__Key). Production keys are additionally held to the RFC 7518 minimum
/// for HMAC-SHA256 (128 bits) so a short/weak key cannot reach a real
/// deployment unnoticed; development keys only need to be non-placeholder.
/// </summary>
public static class JwtKeyValidator
{
    public const string PlaceholderPrefix = "CHANGE_ME";

    /// <summary>Minimum production key length (RFC 7518: >= 128 bits for HMAC-SHA256).</summary>
    public const int ProductionMinKeyLength = 16;

    private static readonly string[] PlaceholderWords =
    {
        "placeholder", "secret", "your-secret-key", "changeme", "change-me", "not-a-real-secret"
    };

    /// <summary>
    /// Returns the validated key, or null when the key is absent entirely
    /// (design-time tooling should not crash before it can even read the
    /// model). Throws InvalidOperationException for any value that must
    /// never be allowed to sign or validate tokens.
    /// </summary>
    public static string? Validate(string? key, bool productionMinimums)
    {
        if (key is null)
            return null; // key not present in this configuration source at all

        if (string.IsNullOrWhiteSpace(key))
            throw ForInvalid("blank or whitespace");

        var trimmed = key.Trim();
        if (trimmed.Length != key.Length)
            throw ForInvalid("surrounded by whitespace");

        if (trimmed.StartsWith(PlaceholderPrefix, StringComparison.OrdinalIgnoreCase))
            throw ForInvalid("the repository placeholder ('CHANGE_ME...') - provide a real key via user secrets or an environment variable");

        foreach (var candidate in PlaceholderWords)
        {
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
                throw ForInvalid($"the known placeholder '{candidate}'");
        }

        if (productionMinimums && trimmed.Length < ProductionMinKeyLength)
            throw ForInvalid($"too short for HMAC-SHA256 (minimum {ProductionMinKeyLength} characters in production)");

        return trimmed;
    }

    private static InvalidOperationException ForInvalid(string reason) =>
        new($"JWT signing key is not usable ({reason}). Set 'Jwt:Key' in user secrets or an environment variable before starting the application.");
}
