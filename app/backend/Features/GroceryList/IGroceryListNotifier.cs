using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Tells everyone looking at the list about a saved change. Hides SignalR from <see cref="GroceryListService"/>,
/// which keeps the service testable without a hub.
/// </summary>
internal interface IGroceryListNotifier
{
    /// <summary>
    /// Sends <paramref name="change"/> to every connected client.
    /// </summary>
    /// <param name="change">The saved change.</param>
    /// <returns>A task that completes when the message has been queued for every connection.</returns>
    public Task ListChangedAsync(GroceryListChange change);
}
