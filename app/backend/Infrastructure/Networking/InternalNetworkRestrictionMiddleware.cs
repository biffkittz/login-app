using System.Net;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Networking;

/// <summary>
/// Refuses (403) any connection whose peer address is outside <see cref="NetworkOptions.InternalNetworks"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is defense in depth. The primary control is the network itself: the Container Apps environment is
/// VNet-internal, so the container is unreachable from the internet. This middleware keeps the app internal-only
/// even if the image is ever run somewhere that does expose it.
/// </para>
/// <para>
/// It deliberately judges the immediate TCP peer (normally the Container Apps ingress) and runs before
/// <c>UseForwardedHeaders</c>. The forwarded client address is not used, because end users may legitimately
/// reach the app through an Application Gateway from outside the VNet.
/// </para>
/// </remarks>
internal sealed class InternalNetworkRestrictionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<IPNetwork> _internalNetworks;
    private readonly ILogger<InternalNetworkRestrictionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InternalNetworkRestrictionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">Validated network settings.</param>
    /// <param name="logger">The logger.</param>
    public InternalNetworkRestrictionMiddleware(
        RequestDelegate next,
        IOptions<NetworkOptions> options,
        ILogger<InternalNetworkRestrictionMiddleware> logger)
    {
        _next = next;
        _internalNetworks = options.Value.ParseInternalNetworks();
        _logger = logger;
    }

    /// <summary>
    /// Passes internal requests on and answers everything else with 403 Forbidden.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        IPAddress? peer = context.Connection.RemoteIpAddress;
        if (peer is not null && IsInternal(peer))
        {
            return _next(context);
        }

        // A missing peer address (no TCP connection, e.g. a Unix socket) is refused too: fail closed.
        NetworkLog.RequestRefused(_logger, peer?.ToString() ?? "(unknown)");
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private bool IsInternal(IPAddress address)
    {
        // Kestrel listens on a dual-stack socket, so IPv4 peers arrive as IPv4-mapped IPv6 addresses
        // (::ffff:10.0.0.4), which an IPv4 range such as 10.0.0.0/8 would otherwise never match.
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        foreach (IPNetwork network in _internalNetworks)
        {
            if (network.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
