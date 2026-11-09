using DyeHouseERP.API.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// P1 (production startup path): what the database bootstrap touches depends on the environment.
/// These tests pin the three decisions that make a Production start usable without making a
/// development, testing or design-time start behave differently:
///   * Production seeds the master data it cannot run without, but does not migrate implicitly and
///     never creates a default login;
///   * Development keeps its documented local login and applies migrations;
///   * Testing and EF design-time runs leave the database completely alone.
/// </summary>
public class DatabaseBootstrapOptionsTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "DyeHouseERP.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static TestHostEnvironment Env(string name) => new() { EnvironmentName = name };

    [Fact]
    public void Development_applies_migrations_seeds_and_keeps_the_documented_local_login()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(Config(), Env("Development"));

        Assert.True(options.Migrate);
        Assert.True(options.SeedMasterData);
        Assert.True(options.DevelopmentDefaultAdmin);
    }

    [Fact]
    public void Production_seeds_the_master_data_but_never_migrates_implicitly_or_creates_a_default_login()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(Config(), Env("Production"));

        // A schema change on boot is a deploy decision, not something a restart should do silently.
        Assert.False(options.Migrate);
        // Without warehouses and document numbering the application cannot post anything at all.
        Assert.True(options.SeedMasterData);
        // There is no default production password: an administrator must be supplied.
        Assert.False(options.DevelopmentDefaultAdmin);
    }

    [Fact]
    public void Configuration_can_turn_migrations_on_for_production_single_instance_deployments()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(
            Config(("Database:MigrateOnStartup", "true")), Env("Production"));

        Assert.True(options.Migrate);
    }

    [Fact]
    public void Configuration_can_turn_every_boot_time_change_off()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(
            Config(("Database:MigrateOnStartup", "false"), ("Database:SeedMasterDataOnStartup", "false")),
            Env("Production"));

        Assert.False(options.Migrate);
        Assert.False(options.SeedMasterData);
        Assert.False(DatabaseBootstrap.ShouldRunOnStartup(Env("Production"), options));
    }

    [Fact]
    public void Bootstrap_credentials_are_read_from_configuration_without_resolving_them_yet()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(
            Config(("Bootstrap:AdminUsername", "first.admin"), ("Bootstrap:AdminPasswordFile", "/run/secrets/admin")),
            Env("Production"));

        Assert.Equal("first.admin", options.AdminUsername);
        Assert.Equal("/run/secrets/admin", options.AdminPasswordFile);
        Assert.Null(options.AdminPassword);
    }

    [Fact]
    public void Testing_environment_is_never_touched_by_the_bootstrap()
    {
        // The integration suite drops and recreates its own schema; a boot-time migration would fight it.
        var options = DatabaseBootstrap.Options.FromConfiguration(Config(), Env("Testing"));

        Assert.False(DatabaseBootstrap.ShouldRunOnStartup(Env("Testing"), options with { Migrate = true, SeedMasterData = true }));
    }

    [Fact]
    public void Ef_design_time_is_never_touched_by_the_bootstrap()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(
            Config(("Database:MigrateOnStartup", "true"), ("Database:SeedMasterDataOnStartup", "true")),
            Env(EfDesignTime.EnvironmentName));

        Assert.False(DatabaseBootstrap.ShouldRunOnStartup(Env(EfDesignTime.EnvironmentName), options));
    }

    [Fact]
    public void A_development_start_still_prepares_the_database()
    {
        var options = DatabaseBootstrap.Options.FromConfiguration(Config(), Env("Development"));

        Assert.True(DatabaseBootstrap.ShouldRunOnStartup(Env("Development"), options));
    }
}
