using DyeHouseERP.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// SQL Server rounds a decimal column without an explicit precision to its default (18,2) without
/// any error, so the loss is only noticed when a report disagrees with the floor's paperwork. The
/// production context throws on that warning (see Persistence/DependencyInjection.cs); this test
/// builds the same model and proves no column triggers it, without needing a database.
/// </summary>
public class DatabaseModelPrecisionTests
{
    [Fact]
    public void Every_decimal_column_declares_the_precision_it_needs()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=(local);Database=DyeHouseERP;Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .ConfigureWarnings(w => w.Throw(SqlServerEventId.DecimalTypeDefaultWarning))
            .Options;

        using var db = new ApplicationDbContext(options);

        // Building the model is what raises the warning; touching it is the assertion.
        var build = () => _ = db.Model;

        build.Should().NotThrow(
            "a decimal column without HasPrecision is silently rounded by SQL Server");
    }
}
