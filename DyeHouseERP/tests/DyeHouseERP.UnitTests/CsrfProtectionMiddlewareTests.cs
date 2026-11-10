using System.Text;
using DyeHouseERP.API.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DyeHouseERP.UnitTests;

public class CsrfProtectionMiddlewareTests
{
    private static (DefaultHttpContext Context, bool NextCalled) Run(
        string method,
        string? csrfCookie,
        string? csrfHeader,
        bool sessionCookie = true,
        bool bearer = false,
        string path = "/api/customers")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        var cookies = new StringBuilder();
        if (sessionCookie) cookies.Append($"{AuthCookies.AccessToken}=jwt");
        if (csrfCookie is not null)
        {
            if (cookies.Length > 0) cookies.Append("; ");
            cookies.Append($"{AuthCookies.Csrf}={csrfCookie}");
        }
        if (cookies.Length > 0) context.Request.Headers.Cookie = cookies.ToString();
        if (csrfHeader is not null) context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = csrfHeader;
        if (bearer) context.Request.Headers.Authorization = "Bearer header-token";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        middleware.InvokeAsync(context).GetAwaiter().GetResult();
        return (context, nextCalled);
    }

    [Fact]
    public void Unsafe_request_with_matching_double_submit_token_passes()
    {
        var (context, nextCalled) = Run("POST", "abc123", "abc123");

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public void Unsafe_request_without_the_header_is_rejected()
    {
        var (context, nextCalled) = Run("POST", "abc123", null);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public void Unsafe_request_with_a_mismatched_token_is_rejected()
    {
        var (context, nextCalled) = Run("DELETE", "abc123", "different");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public void Bearer_authenticated_requests_are_not_csrf_checked()
    {
        var (_, nextCalled) = Run("POST", null, null, bearer: true);

        Assert.True(nextCalled);
    }

    [Fact]
    public void Safe_methods_are_never_blocked()
    {
        var (_, nextCalled) = Run("GET", null, null);

        Assert.True(nextCalled);
    }

    [Fact]
    public void Sign_in_is_never_blocked_by_a_leftover_session()
    {
        // Replacing a session must not require the token of the session being replaced.
        var (_, nextCalled) = Run("POST", null, null, path: "/api/auth/login");

        Assert.True(nextCalled);
    }

    [Fact]
    public void Requests_without_a_session_cookie_are_not_blocked()
    {
        var (_, nextCalled) = Run("POST", null, null, sessionCookie: false);

        Assert.True(nextCalled);
    }
}
