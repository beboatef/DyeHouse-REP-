using System.Net;
using System.Text.Json;
using DyeHouseERP.Application.Auth.Commands;
using DyeHouseERP.Application.Common.Exceptions;
using DyeHouseERP.Domain.Exceptions;

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
