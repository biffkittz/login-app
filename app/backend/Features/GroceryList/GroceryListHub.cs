using System.Security.Claims;
using LoginApp.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The realtime channel for the grocery list: a WebSocket per open list over which the server pushes every saved
/// change (<see cref="IGroceryListClient.ListChanged"/>).
/// </summary>
/// <remarks>
/// <para>
/// Clients call nothing on the hub. Changes go through the HTTP endpoints, which validate, rate-limit and answer
/// with ordinary status codes, and which keep working while a phone's WebSocket is reconnecting. The hub only
/// pushes, which leaves it almost no attack surface.
/// </para>
/// <para>
/// SignalR runs in this process with no backplane, so it reaches only the clients connected to this instance. The
/// app therefore runs as a single instance (Terraform enforces <c>app_max_replicas = 1</c>).
/// </para>
/// </remarks>
/// <param name="dbContext">The application database.</param>
/// <param name="logger">The logger.</param>
[Authorize]
internal sealed class GroceryListHub(ApplicationDbContext dbContext, ILogger<GroceryListHub> logger) : Hub<IGroceryListClient>
{
    /// <summary>The URL path of the hub.</summary>
    public const string Path = "/api/grocery-list/hub";

    /// <summary>
    /// Accepts the connection only from this site's own pages and only for an account that still exists.
    /// </summary>
    /// <returns>A task that completes when the connection has been accepted or refused.</returns>
    public override async Task OnConnectedAsync()
    {
        HttpContext? httpContext = Context.GetHttpContext();
        if (httpContext is not null && IsCrossOrigin(httpContext.Request))
        {
            GroceryListLog.ConnectionRefusedCrossOrigin(logger, httpContext.Request.Headers.Origin.ToString());
            Context.Abort();
            return;
        }

        // Like /api/auth/me, check the database rather than trusting the token: a deleted account loses access at once.
        string? userId = Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId)
            || !await dbContext.Users.AnyAsync(user => user.Id == userId, Context.ConnectionAborted))
        {
            GroceryListLog.ConnectionRefusedUnknownUser(logger);
            Context.Abort();
            return;
        }

        GroceryListLog.ClientConnected(logger, userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Returns whether a request comes from a page on another site, judged by its <c>Origin</c> header.
    /// </summary>
    /// <param name="request">The request that opens the connection.</param>
    /// <returns><see langword="true"/> when an <c>Origin</c> header is present and names a different host.</returns>
    /// <remarks>
    /// Browsers send <c>Origin</c> when opening a WebSocket, and pages can't forge it. A page on another site would be
    /// refused anyway, because the <c>SameSite=Strict</c> auth cookie isn't sent with its request; checking the origin
    /// as well is the defense against cross-site WebSocket hijacking that OWASP recommends, and doesn't depend on
    /// browser cookie rules. Clients other than browsers send no <c>Origin</c> and are judged by their token alone.
    /// </remarks>
    internal static bool IsCrossOrigin(HttpRequest request)
    {
        string? origin = request.Headers.Origin;
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        // Comparing the host and port is enough: a page with the same host and port is this site.
        return !Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri)
            || !string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
