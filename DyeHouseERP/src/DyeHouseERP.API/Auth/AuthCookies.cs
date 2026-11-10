namespace DyeHouseERP.API.Auth;

/// <summary>
/// The two cookies that make up a browser session.
///
/// <see cref="AccessToken"/> is HttpOnly: JavaScript - and therefore an XSS payload - cannot read
/// it, which is the property localStorage could never provide. <see cref="Csrf"/> is deliberately
/// readable, because the SPA must copy it into the X-CSRF-Token header; that is what makes the
/// double-submit check work (a cross-site page can cause the cookie to be sent, but cannot read it).
/// </summary>
public static class AuthCookies
{
    public const string AccessToken = "dyehouse_at";
    public const string RefreshToken = "dyehouse_rt";
    public const string Csrf = "dyehouse_csrf";
}
