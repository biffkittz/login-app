namespace LoginApp.Api.Infrastructure.Security;

/// <summary>
/// Adds browser security headers to every response, including the served SPA.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Vite emits only external, fingerprinted JS and CSS files (no inline <script> or <style>), and the app loads
    // nothing from other origins, so a strict same-origin policy works without 'unsafe-inline' or nonces. Vue's
    // runtime style bindings go through the CSSOM, which style-src does not restrict.
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>
    /// Registers the headers to be written when the response starts, then continues the pipeline.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        // Applied as the response starts rather than now, so the headers also reach responses that later
        // middleware rewrites; the exception handler, for example, clears all headers before writing an error.
        context.Response.OnStarting(
            static state =>
            {
                ApplyHeaders((HttpContext)state);
                return Task.CompletedTask;
            },
            context);

        return next(context);
    }

    private static void ApplyHeaders(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // API responses describe the signed-in user: never let a browser or proxy cache them.
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "no-store";
        }
    }
}
