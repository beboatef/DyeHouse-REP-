using DyeHouseERP.Application.Common.Interfaces;
using DyeHouseERP.Application.Common.Monitoring;
using DyeHouseERP.Domain.Common;
using DyeHouseERP.Domain.Entities;
using DyeHouseERP.Persistence;
using DyeHouseERP.Persistence.Numbering;
using DyeHouseERP.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.API.Startup;

/// <summary>
/// THE startup path that brings a database up to a usable state - the same one in development and
/// in production.
///
/// It does three things, in this order:
///   1. the schema (pending migrations, or EnsureCreated when the repository has no migration yet);
///   2. the master data the application cannot run without: document numbering, the four stock
///      warehouses, purchase units, production stages, machines and shifts. Every seeder is
///      idempotent - it inserts only what is missing and never edits what an administrator changed;
///   3. the FIRST administrator - only when the Users table is still empty, and only from
///      credentials the operator supplies. There is no default production password, and the
///      password is never written to a log.
///
/// Why this exists: every one of those steps used to sit inside <c>if (IsDevelopment())</c> in
/// Program.cs, so a Production deployment started against an empty database - no tables, no
/// warehouses, no login - and said nothing about why.
///
/// The same routine is the deploy step:
///   <c>dotnet run --project src/DyeHouseERP.API -- --bootstrap</c>
/// prepares the database and exits without starting the web server.
/// </summary>
public static class DatabaseBootstrap
{
    /// <summary>A first administrator's password must be at least this long (the dev seed is a documented exception).</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>How long a normal start waits for the database before reporting it as unreachable.</summary>
    public const int ConnectionProbeTimeoutSeconds = 15;

    /// <summary>Resolved from configuration, one instance per start.</summary>
    public sealed record Options
    {
        /// <summary>Apply the schema. Default: on in Development, off in Production (a deploy step is safer there).</summary>
        public bool Migrate { get; init; }

        /// <summary>Seed the master data. Default: on in Development and Production - without it the app is unusable.</summary>
        public bool SeedMasterData { get; init; }

        /// <summary>Development only: seed the documented local login when no user exists, as before.</summary>
        public bool DevelopmentDefaultAdmin { get; init; }

        /// <summary>The bootstrap administrator username, from configuration.</summary>
        public string? AdminUsername { get; init; }
        /// <summary>The bootstrap administrator password, from configuration (never logged).</summary>
        public string? AdminPassword { get; init; }

        /// <summary>A file holding the password, for operators who do not want it in the environment.</summary>
        public string? AdminPasswordFile { get; init; }

        /// <summary>Reads the bootstrap options from configuration and environment.</summary>
        public static Options FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
        {
            var isDevelopment = environment.IsDevelopment();
            var isProduction = environment.IsProduction();

            return new Options
            {
                Migrate = configuration.GetValue<bool?>("Database:MigrateOnStartup") ?? isDevelopment,
                SeedMasterData = configuration.GetValue<bool?>("Database:SeedMasterDataOnStartup")
                                 ?? (isDevelopment || isProduction),
                DevelopmentDefaultAdmin = isDevelopment,
                AdminUsername = configuration["Bootstrap:AdminUsername"],
                AdminPassword = configuration["Bootstrap:AdminPassword"],
                AdminPasswordFile = configuration["Bootstrap:AdminPasswordFile"]
            };
        }
    }

    /// <summary>
    /// Whether a normal start should prepare the database. The integration suite ("Testing") resets
    /// and seeds its own schema, and EF design-time tooling must not touch a database at all, so both
    /// are left alone.
    /// </summary>
    public static bool ShouldRunOnStartup(IHostEnvironment environment, Options options)
    {
        if (environment.IsEnvironment("Testing")) return false;
        if (environment.IsEnvironment(EfDesignTime.EnvironmentName)) return false;
        return options.Migrate || options.SeedMasterData;
    }

    /// <summary>
    /// Runs the bootstrap. Throws on an unrecoverable configuration or schema problem: a half-prepared
    /// database must not come up pretending to be ready.
    /// </summary>
    public static async Task RunAsync(
        IServiceProvider services,
        ILogger logger,
        Options options,
        bool forceAdminCheck = false,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (options.Migrate)
        {
            var migrations = db.Database.GetMigrations().ToList();
            if (migrations.Count > 0)
            {
                logger.LogInformation("Database: applying {Count} migration(s).", migrations.Count);
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                // This repository does not ship an EF migration yet, so MigrateAsync would be a no-op
                // and leave an empty database. Create the schema from the model instead - the same
                // thing the integration suite does. The first migration added later takes over.
                logger.LogWarning(
                    "Database: no EF migration is defined, so the schema is created from the model (EnsureCreated).");
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
        else
        {
            // Starting the web server against a database that is unreachable or has no schema must
            // fail with an instruction, not with the first query's "Invalid object name 'Warehouses'".
            // The connectivity probe is BOUNDED: a server that accepts the socket and then stalls must
            // not turn into a startup that hangs with no message at all.
            logger.LogInformation("Database: checking the connection and the schema.");

            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(ConnectionProbeTimeoutSeconds));

            bool reachable;
            try
            {
                reachable = await db.Database.CanConnectAsync(probeTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                reachable = false;
            }

            if (!reachable)
            {
                // Startup cannot proceed without the database: this is the loudest, earliest
                // DatabaseConnectionFailed - a deploy that points at the wrong server fails here,
                // visibly, instead of serving 500s. The message carries no credential.
                logger.LogCritical(
                    "EventName: {EventName}. Startup cannot reach the configured database.",
                    LogEvents.DatabaseConnectionFailed);
                throw new InvalidOperationException(
                    "Cannot reach the database configured in ConnectionStrings:DefaultConnection. Check the server, " +
                    "the credentials and that the database exists, then prepare it with " +
                    "'dotnet run --project src/DyeHouseERP.API -- --bootstrap' and start the API again.");
            }

            if (!await SchemaExistsAsync(db, cancellationToken))
                throw new InvalidOperationException(
                    "The target database has no DyeHouseERP schema and Database:MigrateOnStartup is off. " +
                    "Prepare it as a deploy step first: dotnet run --project src/DyeHouseERP.API -- --bootstrap " +
                    "(or apply the migrations your own way), then start the API again.");
        }

        if (options.SeedMasterData)
            await SeedMasterDataAsync(services, db, logger);

        await EnsureFirstAdministratorAsync(services, db, options, logger, forceAdminCheck, cancellationToken);
    }

    /// <summary>
    /// Master data only: numbering, the four warehouses, purchase units, stages, machines, shifts.
    /// All of it is insert-if-missing, so running it on every start and on every deploy is safe.
    /// </summary>
    private static async Task SeedMasterDataAsync(IServiceProvider services, ApplicationDbContext db, ILogger logger)
    {
        var seederLogger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Database: ensuring master data (numbering, warehouses, units, stages, machines, shifts).");

        await DocumentSequenceSeeder.SeedAsync(db);
        await WarehouseSeeder.SeedAsync(db);
        await PurchaseUnitSeeder.SeedAsync(db);
        await ProductionStageSeeder.SeedAsync(db, seederLogger);
        await MachineSeeder.SeedAsync(db, seederLogger);
        await ProductionShiftSeeder.SeedAsync(db, seederLogger);
    }

    /// <summary>
    /// Creates the first administrator ONLY into an empty Users table. Never resets, never edits and
    /// never grants rights to an existing account, so leaving the bootstrap credentials in the
    /// environment after the first run cannot be used to add a user later.
    /// </summary>
    private static async Task EnsureFirstAdministratorAsync(
        IServiceProvider services,
        ApplicationDbContext db,
        Options options,
        ILogger logger,
        bool forceAdminCheck,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Database: users already exist; the first-administrator step is skipped.");
            return;
        }

        var passwordHasher = services.GetRequiredService<IPasswordHasher>();

        if (options.DevelopmentDefaultAdmin)
        {
            await UserSeeder.SeedAsync(db, passwordHasher, cancellationToken);
            logger.LogWarning(
                "Database: seeded the development login '{Username}' because no user exists. " +
                "Change that password before this database is used for anything real.",
                UserSeeder.DefaultUsername);
            return;
        }

        var username = options.AdminUsername?.Trim();
        var password = ResolveAdminPassword(options, logger);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            var message =
                "The Users table is empty and no bootstrap administrator was supplied, so nobody can sign in. " +
                "Set Bootstrap:AdminUsername and Bootstrap:AdminPassword (or Bootstrap:AdminPasswordFile) and run " +
                "'dotnet run --project src/DyeHouseERP.API -- --bootstrap', then remove the credential again. " +
                "This application never creates a default password.";

            if (forceAdminCheck) throw new InvalidOperationException(message);
            logger.LogError("{Message}", message);
            return;
        }

        ValidateAdminPassword(username!, password!);

        var bootstrapPasswordHash = passwordHasher.Hash(password!);
        var admin = new User(
            username!,
            bootstrapPasswordHash,
            "Administrator",
            roles: $"admin,{Permissions.InventoryAllowNegativeStock}",
            createdBy: "bootstrap");

        // Part 2: a bootstrap-supplied password arrives through configuration, not from the
        // user's own choice. The account starts flagged and the password lands in history.
        admin.MarkPasswordSetByAdministrator("bootstrap");
        db.Users.Add(admin);
        db.PasswordHistories.Add(new PasswordHistory(admin.Id, bootstrapPasswordHash, "bootstrap"));
        await db.SaveChangesAsync(cancellationToken);

        // Only the username is logged: the password came from configuration and is never repeated.
        logger.LogInformation(
            "Database: created the first administrator '{Username}' from the supplied credentials. " +
            "Rotate its password after signing in, then remove the bootstrap credential.",
            username);
    }

    private static string? ResolveAdminPassword(Options options, ILogger logger)
    {
        if (!string.IsNullOrEmpty(options.AdminPassword)) return options.AdminPassword;

        if (string.IsNullOrWhiteSpace(options.AdminPasswordFile)) return null;

        if (!File.Exists(options.AdminPasswordFile))
            throw new InvalidOperationException(
                $"Bootstrap:AdminPasswordFile points at a file that does not exist ('{options.AdminPasswordFile}').");

        // Trailing newlines only: a secret file written by an editor or a secret manager ends with one.
        var fromFile = File.ReadAllText(options.AdminPasswordFile).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(fromFile))
            throw new InvalidOperationException("Bootstrap:AdminPasswordFile is empty.");
        logger.LogInformation("Database: the administrator password was read from Bootstrap:AdminPasswordFile.");
        return fromFile;
    }

    private static void ValidateAdminPassword(string username, string password)
    {
        if (password.Length < MinimumPasswordLength)
            throw new InvalidOperationException(
                $"Bootstrap:AdminPassword must be at least {MinimumPasswordLength} characters.");

        if (password == UserSeeder.DefaultPassword)
            throw new InvalidOperationException(
                "Bootstrap:AdminPassword must not be the repository's development default password.");

        if (string.Equals(username, password, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bootstrap:AdminPassword must not be the administrator's own username.");
    }

    /// <summary>
    /// True when the application's tables are present. A missing database or a missing table both
    /// answer false, and both are reported by the caller with the same instruction.
    /// </summary>
    private static async Task<bool> SchemaExistsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var found = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Warehouses'")
                .ToListAsync(cancellationToken);
            return found.Count > 0 && found[0] > 0;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
