using System.Text;
using Microsoft.EntityFrameworkCore;
using DyeHouseERP.API.Middleware;
using DyeHouseERP.Application;
using DyeHouseERP.Infrastructure;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
