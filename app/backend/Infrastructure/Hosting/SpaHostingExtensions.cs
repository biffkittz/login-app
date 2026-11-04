namespace LoginApp.Api.Infrastructure.Hosting;

/// <summary>
/// Serves the compiled Vue single-page app from <c>wwwroot</c>.
/// </summary>
/// <remarks>
/// Serving the SPA from the API keeps both on one origin, which is what allows the auth cookie to be
/// <c>SameSite=Strict</c> and removes any need for CORS. During development the Vite dev server serves the SPA
/// instead and proxies <c>/api</c> here, preserving the same single-origin arrangement.
/// </remarks>
internal static class SpaHostingExtensions
{
    private const string ShellFile = "index.html";

    // A catch-all route that matches client-side routes only. 'nonfile' skips paths that look like files
    // (/assets/missing.js stays a 404). The regex skips everything under /api, so API requests that match no
    // endpoint keep routing's own answers, such as a JSON 404 for an unknown route or a 415 for a non-JSON body,
    // rather than the SPA shell with a misleading 200.
    private const string ClientRoutePattern = "{*path:nonfile:regex(^(?!api(/|$)))}";

    // The shell names the current build's fingerprinted JS/CSS files, so browsers must revalidate it on every
    // visit; otherwise a cached copy keeps loading the previous release after a deployment.
    private static readonly StaticFileOptions s_shellOptions = new()
    {
        OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-cache",
    };

    /// <summary>
    /// Maps the static frontend assets and the SPA fallbacks.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapSpaFrontend(this WebApplication app)
    {
        // buildAndRunSite.sh and the Dockerfile copy frontend/dist into wwwroot *before* `dotnet publish`, so the
        // files are known at build time and MapStaticAssets can serve them with ETags and precompressed
        // gzip/Brotli variants.
        app.MapStaticAssets();

        // Any other client route (/login, /register, ...) gets index.html and Vue Router renders the page.
        app.MapFallbackToFile(ClientRoutePattern, ShellFile, s_shellOptions);

        // The site root needs its own mapping: for "/" the catch-all value is empty, and the regex constraint
        // rejects empty values.
        app.MapFallbackToFile("/", ShellFile, s_shellOptions);

        return app;
    }
}
