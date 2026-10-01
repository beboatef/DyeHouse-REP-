using Xunit;

namespace DyeHouseERP.IntegrationTests;

/// <summary>
/// Every integration test class shares the single database
/// DyeHouseERP_IntegrationTests, whose lifecycle is drop/recreate per class
/// (EnsureDeletedAsync + EnsureCreatedAsync in CustomWebApplicationFactory).
/// xUnit runs classes inside a collection sequentially, so placing every
/// concrete test class in this collection stops parallel classes from racing
/// the same database - the source of the "ALTER DATABASE ... permission/access
/// check failed" and "Database already exists" failures when SQL Server is
/// the real provider.
///
/// This does NOT disable parallelism inside the test scenarios themselves:
/// the Task.WhenAll HTTP race workers in the concurrency suite run at the
/// application level (real concurrent requests into the in-process API) and
/// are unaffected by xUnit collection scheduling.
///
/// Note: [Collection] is NOT inherited from base classes in xUnit, so it must
/// appear on every concrete test class that derives from IntegrationTestBase.
/// </summary>
[CollectionDefinition("IntegrationDatabase")]
public sealed class IntegrationDatabaseCollection { }
