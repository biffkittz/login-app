using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace LoginApp.Api.Infrastructure.Security;

/// <summary>
/// Server-level hardening that applies in every environment, so it is set in code rather than configuration.
/// </summary>
internal static class ServerHardeningExtensions
{
    // The largest legitimate request is a grocery item with long notes, a few kilobytes. A small cap stops clients
    // from tying up the server with huge bodies (Kestrel's default allows 30 MB).
    private const long MaxRequestBodyBytes = 64 * 1024;

    // Each open grocery list holds a WebSocket. A family needs a handful; the cap stops a flood of connections from
    // exhausting the container's memory (Kestrel's default is unlimited).
    private const long MaxConcurrentWebSocketConnections = 1000;

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
            kestrel.Limits.MaxConcurrentUpgradedConnections = MaxConcurrentWebSocketConnections;
        });

        // UseHsts adds this header to HTTPS responses (Kestrel's own HTTPS in Azure, or the scheme restored by
        // UseForwardedHeaders behind another TLS-terminating proxy), so browsers refuse plain HTTP afterwards.
        services.AddHsts(static hsts => hsts.MaxAge = TimeSpan.FromDays(365));

        return services;
    }
}
