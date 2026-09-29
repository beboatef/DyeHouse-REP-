using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DyeHouseERP.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DyeHouseERP.Infrastructure.Services;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration) => _configuration = configuration;

    public string GenerateToken(Guid userId, string username, IEnumerable<string> roles, out DateTime expiresAtUtc)
    {
        // Mandatory, fail-fast: the signing key must come from configuration.
        // There is deliberately no fallback - a missing/blank/placeholder
        // 'Jwt:Key' is a configuration error and must stop the token flow
        // (B1 + H2). Validation in Program.cs goes through the same validator,
        // so issued tokens and accepted tokens always share one key.
        var key = JwtKeyValidator.Validate(_configuration.GetSection("Jwt")["Key"], productionMinimums: false)
            ?? throw new InvalidOperationException(
                "JWT signing key is not configured. Set 'Jwt:Key' in appsettings, user secrets or an environment variable before issuing tokens.");

        var jwtSection = _configuration.GetSection("Jwt");
        expiresAtUtc = DateTime.UtcNow.AddHours(8);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.Name, username),
            new("preferred_username", username)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
