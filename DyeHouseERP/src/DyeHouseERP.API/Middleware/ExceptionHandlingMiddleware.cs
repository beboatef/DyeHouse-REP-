using System.Net;
using System.Text.Json;
using DyeHouseERP.Application.Auth.Commands;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DyeHouseERP.API.Middleware;

/// <summary>
/// Translates exceptions thrown anywhere in the pipeline into a consistent
/// JSON error envelope, and picks the right HTTP status code per exception
/// type instead of leaking 500s for ordinary business-rule violations.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        object payload;

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                payload = new { title = "Validation failed", status = 400, errors = validationEx.Errors };
                break;

            case InvalidCredentialsException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                payload = new { title = "Invalid username or password.", status = 401 };
                break;

            case NotFoundException notFoundEx:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                payload = new { title = notFoundEx.Message, status = 404 };
                break;

            case NegativeStockException stockEx:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                payload = new
                {
                    title = stockEx.Message,
                    status = 409,
                    code = "NEGATIVE_STOCK",
                    currentBalance = stockEx.CurrentBalance,
                    requestedQuantity = stockEx.RequestedQuantity,
                    shortage = stockEx.Shortage
                };
                break;

            case StockLockTimeoutException:
                // R4: the stock/document locks (sp_getapplock) throw
                // StockLockTimeoutException when another operation has held the
                // same stock dimension past the wait budget. That is a "somebody
                // else is working on this right now" condition, so it is
                // reported as a conflict the client can retry - not as a server
                // fault. Only this specific type maps to LOCK_TIMEOUT; unrelated
                // timeouts stay server faults.
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                payload = new
                {
                    title = "This stock is locked by another operation in progress. Please retry in a moment.",
                    status = 409,
                    code = "LOCK_TIMEOUT"
                };
                break;

            case DbUpdateConcurrencyException:
                // H1: someone else committed a change to the same row first.
                // That is a normal "you are working on stale data" conflict, not
                // a server fault - report it as 409 so the client reloads
                // instead of showing an opaque 500.
                // NOTE: evaluated BEFORE DbUpdateException (its base class) so
                // it is never swallowed by the duplicate-key branch below.
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                payload = new
                {
                    title = "This record was changed by someone else while you were working on it. Reload and try again.",
                    status = 409,
                    code = "CONCURRENT_UPDATE"
                };
                break;

            case DbUpdateException { InnerException: SqlException sqlEx } when sqlEx.Number is 2601 or 2627:
                // Unique index / primary key violation: the friendly check ran
                // concurrently with another identical post and the DB backstop
                // won. Reported as a conflict, with NO SQL text in the payload.
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                payload = new
                {
                    title = "This record duplicates an existing entry. Adjust the value and try again.",
                    status = 409,
                    code = "DUPLICATE_RECORD"
                };
                break;

            case DuplicateCodeException or DocumentLockedException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                payload = new { title = exception.Message, status = 409 };
                break;

            case DomainException domainEx:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                payload = new { title = domainEx.Message, status = 422 };
                break;

            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                payload = new { title = "You do not have permission to perform this action.", status = 403 };
                break;

            default:
                _logger.LogError(exception, "Unhandled exception");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                payload = new { title = "An unexpected error occurred.", status = 500 };
                break;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
