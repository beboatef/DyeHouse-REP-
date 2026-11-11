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

    /// <summary>
    /// Refresh lifetime is configuration for the same reason the access lifetime is: the deployment
    /// decides the tradeoff. 7 days keeps a daily user signed in across a working week; it is
    /// rotation plus reuse detection - not this number - that contains a stolen token.
    /// </summary>
    public TimeSpan RefreshTokenLifetime
    {
        get
        {
            var days = int.TryParse(_configuration.GetSection("Jwt")["RefreshTokenDays"], out var configured) && configured > 0
                ? configured
                : 7;
            return TimeSpan.FromDays(days);
        }
    }

    /// <summary>
    /// The race window for concurrent refreshes. Browsers share cookies across tabs, so two tabs
    /// that 401 at the same moment both present the token that was current when their requests
    /// left; one rotation wins and the loser looks exactly like a stolen token. 30 seconds covers
    /// that race without giving a real attacker a useful window. 0 turns the grace off.
    /// </summary>
    public TimeSpan RefreshReuseGrace
    {
        get
        {
            var seconds = int.TryParse(_configuration.GetSection("Jwt")["RefreshReuseGraceSeconds"], out var configured) && configured >= 0
                ? configured
                : 30;
            return TimeSpan.FromSeconds(seconds);
        }
    }

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

        // The lifetime is configuration, not a constant: a stolen cookie (or a lost laptop) is only
        // useful until this expires, so the deployment decides how short it can afford to be.
        // 30 minutes is the default because the SPA re-authenticates on a 401; 8 hours was the old
        // hardcoded value and gave an attacker a full working day with one stolen token.
        var lifetimeMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var configured) && configured > 0
            ? configured
            : 30;
        expiresAtUtc = DateTime.UtcNow.AddMinutes(lifetimeMinutes);

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
