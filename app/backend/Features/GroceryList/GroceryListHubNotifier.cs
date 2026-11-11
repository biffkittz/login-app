using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Broadcasts list changes through <see cref="GroceryListHub"/>.
/// </summary>
/// <param name="hub">The hub's context, usable outside a hub method.</param>
/// <param name="metrics">Counts the broadcasts and the messages they send.</param>
internal sealed class GroceryListHubNotifier(IHubContext<GroceryListHub, IGroceryListClient> hub, GroceryListMetrics metrics)
    : IGroceryListNotifier
{
    /// <inheritdoc />
    /// <remarks>
    /// Every connection gets the change, including the author's. The SPA applies its own change from the HTTP
    /// response too; items carry a version, so whichever copy arrives second is recognized and ignored. SignalR
    /// handles a failed send by closing that one connection (its client then reconnects and reloads the list), so
    /// one broken phone can't make the write fail.
    /// </remarks>
    public async Task ListChangedAsync(GroceryListChange change)
    {
        long recipients = metrics.OpenConnections;
        await hub.Clients.All.ListChanged(change);
        metrics.ListChangeBroadcast(change, recipients);
    }

    /// <inheritdoc />
    public async Task SettingsChangedAsync(GroceryListSettingsResponse settings)
    {
        long recipients = metrics.OpenConnections;
        await hub.Clients.All.SettingsChanged(settings);
        metrics.SettingsChangeBroadcast(recipients);
    }
}
