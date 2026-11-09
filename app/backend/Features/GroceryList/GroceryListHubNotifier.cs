using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Broadcasts list changes through <see cref="GroceryListHub"/>.
/// </summary>
/// <param name="hub">The hub's context, usable outside a hub method.</param>
internal sealed class GroceryListHubNotifier(IHubContext<GroceryListHub, IGroceryListClient> hub) : IGroceryListNotifier
{
    /// <inheritdoc />
    /// <remarks>
    /// Every connection gets the change, including the author's. The SPA applies its own change from the HTTP
    /// response too; items carry a version, so whichever copy arrives second is recognized and ignored. SignalR
    /// handles a failed send by closing that one connection (its client then reconnects and reloads the list), so
    /// one broken phone can't make the write fail.
    /// </remarks>
    public Task ListChangedAsync(GroceryListChange change) => hub.Clients.All.ListChanged(change);

    /// <inheritdoc />
    public Task SettingsChangedAsync(GroceryListSettingsResponse settings) => hub.Clients.All.SettingsChanged(settings);
}
