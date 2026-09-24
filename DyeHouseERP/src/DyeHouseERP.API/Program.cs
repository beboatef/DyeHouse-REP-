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
var jwtSection = builder.Configuration.GetSection("Jwt");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"] ?? "dev-only-placeholder-key-change-me-0123456789"))
        };
    });
builder.Services.AddAuthorization();
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

        var passwordHasher = scope.ServiceProvider.GetRequiredService<DyeHouseERP.Application.Common.Interfaces.IPasswordHasher>();
        await UserSeeder.SeedAsync(db, passwordHasher);
    }
}

app.Run();

// Exposes the top-level Program as a type WebApplicationFactory<Program> can
// reference from the integration tests project.
public partial class Program { }
