namespace LoginApp.Api.Infrastructure.Security;

/// <summary>
/// Registers <see cref="SecurityHeadersMiddleware"/>.
/// </summary>
internal static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds the security headers middleware to the pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
