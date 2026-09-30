using System.Reflection;
using System.Text;
using System.Text.Json;
using DyeHouseERP.API.Middleware;
using DyeHouseERP.Application.Common.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// R4 hardening: only <see cref="StockLockTimeoutException"/> maps to 409
/// LOCK_TIMEOUT (a plain TimeoutException must stay a server fault), and a
/// SQL unique-index violation (2601/2627) surfaces as 409 DUPLICATE_RECORD
/// instead of an opaque 500 - with no SQL text leaking into the payload.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private static (HttpContext Context, MemoryStream Body) NewContext()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        context.Response.ContentType = "application/json";
        return (context, body);
    }

    private static async Task<(int Status, JsonElement Payload)> RunAsync(Exception thrown)
    {
        var (context, body) = NewContext();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw thrown,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        body.Position = 0;

        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(body.ToArray())).RootElement;
        return (context.Response.StatusCode, payload.Clone());
    }

    [Fact]
    public async Task StockLockTimeoutException_Returns_409_LockTimeout()
    {
        var (status, payload) = await RunAsync(
            new StockLockTimeoutException("Stock:abc", "Could not acquire the stock lock 'Stock:abc' within 15s."));

        status.Should().Be(409);
        payload.GetProperty("code").GetString().Should().Be("LOCK_TIMEOUT");
        payload.GetProperty("status").GetInt32().Should().Be(409);
    }

    [Fact]
    public async Task Plain_TimeoutException_Does_Not_Map_To_LockTimeout()
    {
        // Any other timeout (e.g. the document-numbering lock, an HTTP client
        // timeout) is a genuine server fault and must NOT be reported as a
        // stock conflict the client can retry.
        var (status, payload) = await RunAsync(new TimeoutException("Something else timed out."));

        status.Should().Be(500);
        payload.TryGetProperty("code", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task Sql_UniqueConstraint_Violation_Returns_409_DuplicateRecord_Without_Sql_Text(int errorNumber)
    {
        var (status, payload) = await RunAsync(
            new DbUpdateException("An error occurred while saving the entity changes.",
                BuildSqlException(errorNumber)));

        status.Should().Be(409);
        payload.GetProperty("code").GetString().Should().Be("DUPLICATE_RECORD");
        payload.GetProperty("status").GetInt32().Should().Be(409);

        var raw = payload.GetRawText();
        raw.Should().NotContain("SqlException");
        raw.Should().NotContain("severity");
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_Is_Evaluated_Before_The_Duplicate_Branch()
    {
        // DbUpdateConcurrencyException derives from DbUpdateException, so it
        // must be matched first - otherwise a concurrency conflict without a
        // SqlException inner would fall through to the default 500.
        var (status, payload) = await RunAsync(new DbUpdateConcurrencyException("Rowversion conflict."));

        status.Should().Be(409);
        payload.GetProperty("code").GetString().Should().Be("CONCURRENT_UPDATE");
    }

    [Fact]
    public async Task DbUpdateException_Without_Sql_Unique_Error_Stayss_500()
    {
        var (status, payload) = await RunAsync(new DbUpdateException("Some other failure.", innerException: null));

        status.Should().Be(500);
        payload.TryGetProperty("code", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DbUpdateException_With_Unrelated_Sql_Error_Stayss_500()
    {
        var (status, _) = await RunAsync(
            new DbUpdateException("An error occurred while saving the entity changes.",
                BuildSqlException(547))); // FK violation, not a duplicate key

        status.Should().Be(500);
    }

    /// <summary>
    /// Builds a genuine Microsoft.Data.SqlClient.SqlException (the type's only
    /// public constructor is the (string, Exception) serialization one, whose
    /// result does not wire up Number), via its internal factory - exactly how
    /// the driver itself creates it.
    /// </summary>
    internal static SqlException BuildSqlException(int number)
    {
        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var errors = (System.Collections.IList)typeof(SqlErrorCollection)
            .GetField("_errors", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(collection)!;

        errors.Add(errorCtor.Invoke(new object?[]
        {
            number, (byte)14, (byte)16, "TestServer", "Cannot insert duplicate key row", "", 1, (uint)0, null
        }));

        var create = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            new[] { typeof(SqlErrorCollection), typeof(string) },
            modifiers: null)!;

        return (SqlException)create.Invoke(null, new object?[] { collection, "16.0.1000" });
    }
}
