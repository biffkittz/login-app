using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Writes and deletes the cookie that carries the access token.
/// </summary>
/// <remarks>
/// <para>
/// Why the JWT travels in a cookie instead of being handed to JavaScript: a token kept in <c>localStorage</c> can
/// be read by any injected script (XSS), whereas an <c>HttpOnly</c> cookie is invisible to scripts entirely.
/// </para>
/// <para>
/// <c>SameSite=Strict</c> stops browsers from attaching the cookie to requests started by other sites. Together
/// with JSON-only endpoints (a cross-site HTML form cannot send <c>application/json</c>) and the absence of any
/// CORS policy, this defeats cross-site request forgery without separate antiforgery tokens. It works because the
/// SPA and the API share one origin: the API serves the built SPA, and the Vite dev server proxies <c>/api</c>.
/// </para>
/// </remarks>
/// <param name="options">Validated cookie settings.</param>
internal sealed class AuthCookieManager(IOptions<AuthCookieOptions> options)
{
    private readonly AuthCookieOptions _options = options.Value;

    /// <summary>
    /// Stores <paramref name="token"/> in the auth cookie, expiring together with the token.
    /// </summary>
    /// <param name="response">The response to add the <c>Set-Cookie</c> header to.</param>
    /// <param name="token">The token to store.</param>
    public void Append(HttpResponse response, AccessToken token) =>
        response.Cookies.Append(_options.Name, token.Value, CreateCookieOptions(token.ExpiresAt));

    /// <summary>
    /// Deletes the auth cookie, signing the browser out.
    /// </summary>
    /// <param name="response">The response to add the expiring <c>Set-Cookie</c> header to.</param>
    /// <remarks>
    /// The deletion repeats the cookie's attributes because browsers ignore a deletion whose Path or Secure flag
    /// differs from the original (and the <c>__Host-</c> prefix rules apply to every <c>Set-Cookie</c>).
    /// </remarks>
    public void Delete(HttpResponse response) =>
        response.Cookies.Delete(_options.Name, CreateCookieOptions(expires: null));

    private CookieOptions CreateCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = _options.Secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
    };
}
