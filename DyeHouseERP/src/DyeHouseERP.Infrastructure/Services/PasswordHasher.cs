using System.Security.Cryptography;
using DyeHouseERP.Application.Common.Interfaces;

namespace DyeHouseERP.Infrastructure.Services;

/// <summary>
/// PBKDF2 (RFC 2898) password hashing with a random per-password salt.
/// Format stored: "{iterations}.{base64 salt}.{base64 hash}" - self-describing
/// so the iteration count can be raised later without breaking existing hashes.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    /// <summary>
    /// A hash that no password can produce (its salt is random and discarded). Verifying against it
    /// costs the same PBKDF2 work as verifying a real row, which is the point: login must not be
    /// measurably faster for a username that does not exist.
    /// Computed once per process, on first use.
    /// </summary>
    private static readonly string DecoyHash = CreateDecoyHash();

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        // A stored hash that is empty, truncated or corrupt is "this password does not match",
        // never an exception: a damaged row must not turn a login attempt into a 500.
        if (string.IsNullOrWhiteSpace(passwordHash)) return false;

        var parts = passwordHash.Split('.', 3);
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out var iterations) || iterations <= 0) return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedHash.Length == 0) return false;

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public bool VerifyOrDecoy(string password, string? passwordHash)
        => Verify(password, passwordHash ?? DecoyHash);

    private static string CreateDecoyHash()
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            "decoy-password-that-is-never-accepted", salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
