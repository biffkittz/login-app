using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace LoginApp.Api.Infrastructure.Security;

/// <summary>
/// Server-level hardening that applies in every environment, so it is set in code rather than configuration.
/// </summary>
internal static class ServerHardeningExtensions
{
    // The largest legitimate request is a registration form of a few hundred bytes. A small cap stops clients
    // from tying up the server with huge bodies (Kestrel's default allows 30 MB).
    private const long MaxRequestBodyBytes = 64 * 1024;

    /// <summary>
    /// Configures Kestrel and HSTS.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddServerHardening(this IServiceCollection services)
    {
        services.Configure<KestrelServerOptions>(static kestrel =>
        {
            // Don't advertise the server software ("Server: Kestrel") to potential attackers.
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });

        // TLS terminates upstream, so Kestrel itself never serves HTTPS. Once UseForwardedHeaders has restored the
        // original scheme, UseHsts adds this header to HTTPS responses so browsers refuse plain HTTP afterwards.
        services.AddHsts(static hsts => hsts.MaxAge = TimeSpan.FromDays(365));

        return services;
    }
}
