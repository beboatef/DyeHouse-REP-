using System.Security.Cryptography;
using System.Text;

namespace DyeHouseERP.Application.Auth;

/// <summary>
/// Opaque refresh tokens: 256 bits of randomness, hex-encoded so they are cookie-safe without
/// further encoding. Only the SHA-256 hash is ever stored - a database dump must not contain a
/// single usable session.
/// </summary>
internal static class RefreshTokenCodec
{
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
