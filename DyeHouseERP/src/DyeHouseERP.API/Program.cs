using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using DyeHouseERP.API.Middleware;
using DyeHouseERP.Application;
using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Infrastructure;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog structured logging ----------
builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/dyehouse-erp-.log", rollingInterval: RollingInterval.Day));

// ---------- Layered service registration (Clean Architecture composition root) ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddPersistence(builder.Configuration);

// ---------- Auth ----------
// The JWT signing key is mandatory and must never be a known placeholder
// (B1 + H2): a missing key stops design-time EF tooling early, while a blank,
// whitespace-padded, or repository-placeholder key ('CHANGE_ME...') fails fast
// with a clear message instead of ever running with a publicly-known secret.
// Both token signing (JwtTokenService) and validation (below) go through the
// same JwtKeyValidator so they always share one key and one set of rules.
var jwtSection = builder.Configuration.GetSection("Jwt");
var isProduction = builder.Environment.IsProduction();
var jwtKey = DyeHouseERP.Infrastructure.Services.JwtKeyValidator.Validate(
    jwtSection["Key"],
    productionMinimums: isProduction);

if (jwtKey is null && !IsEfDesignTime())
{
    // No key in any configuration source: every real start (development or
    // production) must provide one explicitly - there is no fallback (B1/H2).
    throw new InvalidOperationException(
        "JWT signing key is not configured. Set 'Jwt:Key' via user secrets (development) " +
        "or an environment variable / secret provider (production) before starting the application.");
}

// Only EF design-time tooling (dotnet ef) can reach here with a null key; the
// app never starts in that mode, so this string is never used to sign or
// validate anything - it merely lets DI build the JwtBearer options object.
jwtKey ??= "ef-design-time-only-never-used-for-tokens";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // H5: a JWT is self-contained, so on its own it keeps working for its
        // full 8-hour lifetime even after the account is deactivated or its
        // permissions are changed. Re-check the live user row on every request
        // so revocation takes effect immediately. This costs one indexed
        // primary-key lookup and runs before the controller, not after.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<IUserSessionValidator>();
                var userId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                             ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!Guid.TryParse(userId, out var parsedUserId))
                {
                    context.Fail("The token does not identify a user.");
                    return;
                }

                var session = await validator.GetSessionStateAsync(parsedUserId, context.HttpContext.RequestAborted);

                if (session is null)
                {
                    context.Fail("The account no longer exists.");
                    return;
                }

                if (!session.IsActive)
                {
                    context.Fail("The account has been deactivated.");
                    return;
                }

                // Permissions changed after the token was issued: the token's
                // role claims no longer describe what the user may do, so the
                // session is rejected instead of running with stale rights.
                // Both the mapped and the raw claim type are read so the check
                // behaves identically whether or not inbound claim mapping is on.
                var tokenRoles = context.Principal!.FindAll(ClaimTypes.Role)
                    .Concat(context.Principal!.FindAll("role"))
                    .Select(c => c.Value)
                    .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var liveRoles = session.Roles
                    .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!tokenRoles.SequenceEqual(liveRoles, StringComparer.OrdinalIgnoreCase))
                    context.Fail("The account's permissions have changed - sign in again.");
            }
        };
    });
builder.Services.AddAuthorization();

// ---------- Login throttle (B3) ----------
// Failure-driven lockout for POST /api/auth/login only - every other endpoint
// is untouched. Limits come from configuration ("LoginThrottle" section) with
// safe defaults. The state is per-process in-memory: this deployment runs a
// single API instance; a load-balanced deployment would need a shared store
// for a global budget and currently does not.
builder.Services.Configure<DyeHouseERP.API.Auth.LoginThrottleOptions>(
    builder.Configuration.GetSection(DyeHouseERP.API.Auth.LoginThrottleOptions.SectionName));
builder.Services.AddSingleton<DyeHouseERP.API.Auth.LoginThrottleOptions>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DyeHouseERP.API.Auth.LoginThrottleOptions>>().Value);
builder.Services.AddSingleton<DyeHouseERP.API.Auth.LoginRateLimiter>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, DyeHouseERP.API.Authorization.PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, DyeHouseERP.API.Authorization.PermissionAuthorizationHandler>();

// ---------- CORS (React dev server / LAN clients) ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("DyeHouseERPClients", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173" };
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums serialize as their string names (e.g. "RawMaterial", "Accepted",
        // "Pending") rather than raw integers - the React frontend's TypeScript
        // types and every string comparison in it (e.g. status === "Pending")
        // assume this. Without it, every enum field in every DTO would come
        // back as a number and silently break the UI.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "DyeHouse ERP API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
});

var app = builder.Build();

// ---------- Reverse proxy / real client IP (M8) ----------
// Behind a reverse proxy every request otherwise arrives with the PROXY's IP,
// which would (a) make the login throttle treat all users as one client and
// (b) make the audit log record the wrong actor IP.
//
// Forwarded headers are only honored for explicitly trusted hops. Trusting
// X-Forwarded-For from unknown senders would let any caller forge a fresh IP on
// every request and walk straight through the brute-force throttle, so an
// empty/absent configuration means the headers stay ignored and the connection
// IP is used. When no section is present at all, loopback is assumed - the
// development topology (Vite dev proxy on 127.0.0.1) - which is safe because a
// remote attacker can never present a loopback socket address.
var knownProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<IPAddress[]>();
if (knownProxies is null)
    knownProxies = new[] { IPAddress.Loopback, IPAddress.IPv6Loopback };

if (knownProxies.Length > 0)
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        // No KnownNetworks: networks are range-based and easy to over-grant, so
        // trust is expressed as an explicit proxy list only.
        RequireHeaderSymmetry = true
    };
    foreach (var proxy in knownProxies)
        forwardedOptions.KnownProxies.Add(proxy);

    app.UseForwardedHeaders(forwardedOptions);
}

// ---------- Global exception handling -> consistent JSON error envelope ----------
app.UseMiddleware<ExceptionHandlingMiddleware>();

// ---------- Login brute-force throttle (B3): POST /api/auth/login only ----------
// Before UseAuthentication so locked-out requests never reach auth logic.
app.UseMiddleware<DyeHouseERP.API.Auth.LoginThrottleMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("DyeHouseERPClients");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ---------- Apply migrations + seed the numbering engine on startup ----------
// NOTE: for production, prefer running migrations as an explicit deploy
// step rather than on app start. Left here for local/dev convenience.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (app.Environment.IsDevelopment())
    {
        await db.Database.MigrateAsync();
        await DocumentSequenceSeeder.SeedAsync(db);
        await DyeHouseERP.Persistence.Seeding.WarehouseSeeder.SeedAsync(db);
        await DyeHouseERP.Persistence.Seeding.PurchaseUnitSeeder.SeedAsync(db);
        await DyeHouseERP.Persistence.Seeding.ProductionStageSeeder.SeedAsync(
            db, app.Services.GetRequiredService<ILogger<Program>>());

        var passwordHasher = scope.ServiceProvider.GetRequiredService<DyeHouseERP.Application.Common.Interfaces.IPasswordHasher>();
        await UserSeeder.SeedAsync(db, passwordHasher);
    }
}

app.Run();

// H2: 'dotnet ef' builds this Program to discover the DbContext without ever
// serving requests, so a missing JWT key must not break migrations tooling.
// Every other entry point (real starts) is validated strictly above.
static bool IsEfDesignTime() =>
    string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        EfDesignTime.EnvironmentName,
        StringComparison.OrdinalIgnoreCase);

// Exposes the top-level Program as a type WebApplicationFactory<Program> can
// reference from the integration tests project.
public partial class Program { }

/// <summary>Constants for the EF design-time handshake used by Program.cs (H2).</summary>
public static class EfDesignTime
{
    public const string EnvironmentName = "EfDesignTime";
}
