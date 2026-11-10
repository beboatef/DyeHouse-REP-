using System.Text;
using DyeHouseERP.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DyeHouseERP.UnitTests;

/// <summary>
/// The headers are the only thing standing between a JSON response and a browser that tries to
/// interpret it as something executable, so their presence (and their absence on Swagger) is
/// pinned here rather than discovered after a deployment.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpContext> InvokeAsync(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        return context;
    }

    [Fact]
    public async Task Every_response_is_hardened_against_sniffing_and_framing()
    {
        var context = await InvokeAsync("/api/customers");

        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        context.Response.Headers["Cross-Origin-Resource-Policy"].ToString().Should().Be("same-origin");
        context.Response.Headers["Permissions-Policy"].ToString().Should().Contain("camera=()");
    }

    [Fact]
    public async Task Api_json_carries_a_deny_all_content_security_policy()
    {
        var context = await InvokeAsync("/api/auth/login");

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("default-src 'none'");
        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("base-uri 'none'");
    }

    [Fact]
    public async Task Swagger_is_not_given_the_api_policy_that_would_break_its_own_assets()
    {
        var context = await InvokeAsync("/swagger/index.html");

        context.Response.Headers["Content-Security-Policy"].ToString().Should().BeEmpty();
        // The rest of the hardening still applies to documentation pages.
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }
}
