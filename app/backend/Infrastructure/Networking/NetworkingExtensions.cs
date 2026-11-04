using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace LoginApp.Api.Infrastructure.Networking;

/// <summary>
/// Registers internal-network enforcement and trusted reverse-proxy handling.
/// </summary>
internal static class NetworkingExtensions
{
    /// <summary>
    /// Adds validated <see cref="NetworkOptions"/> and configures forwarded-header processing to trust proxies on
    /// the internal networks.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInternalNetworking(this IServiceCollection services)
    {
        services.AddOptions<NetworkOptions>()
            .BindConfiguration(NetworkOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<NetworkOptions>>(ConfigureForwardedHeaders);

        return services;
    }

    /// <summary>
    /// Adds <see cref="InternalNetworkRestrictionMiddleware"/> unless
    /// <see cref="NetworkOptions.RestrictToInternalNetworks"/> is false.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    /// <remarks>Must run before <c>UseForwardedHeaders</c>, which replaces the peer address it inspects.</remarks>
    public static IApplicationBuilder UseInternalNetworkRestriction(this IApplicationBuilder app)
    {
        NetworkOptions options = app.ApplicationServices.GetRequiredService<IOptions<NetworkOptions>>().Value;
        return options.RestrictToInternalNetworks
            ? app.UseMiddleware<InternalNetworkRestrictionMiddleware>()
            : app;
    }

    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions forwarded, IOptions<NetworkOptions> network)
    {
        // TLS terminates at Application Gateway / the Container Apps ingress, and Kestrel only ever sees plain HTTP
        // on port 80. These headers restore the real client address (for rate limiting and logs) and scheme (so
        // HSTS is emitted for HTTPS requests).
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // A request crosses two proxies in Azure (Application Gateway, then the Container Apps ingress). The
        // default ForwardLimit of 1 would stop at the gateway's address, so keep walking back through hops that
        // come from trusted networks until reaching the first untrusted address: the real client.
        forwarded.ForwardLimit = null;

        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        foreach (IPNetwork internalNetwork in network.Value.ParseInternalNetworks())
        {
            forwarded.KnownIPNetworks.Add(internalNetwork);
        }
    }
}
