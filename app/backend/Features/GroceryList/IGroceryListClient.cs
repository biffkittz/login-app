using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The messages <see cref="GroceryListHub"/> sends to connected browsers (a strongly typed SignalR client).
/// </summary>
/// <remarks>
/// Public only because SignalR implements this interface at run time in a separate, generated assembly.
/// </remarks>
public interface IGroceryListClient
{
    /// <summary>
    /// Tells the client that the list changed. Sent to every connection, the author's included, after the change has
    /// been saved.
    /// </summary>
    /// <param name="change">What changed.</param>
    /// <returns>A task that completes when the message has been queued for sending.</returns>
    public Task ListChanged(GroceryListChange change);

    /// <summary>
    /// Tells the client that someone changed the list's settings, such as turning AI pictures off.
    /// </summary>
    /// <param name="settings">The settings as they are now.</param>
    /// <returns>A task that completes when the message has been queued for sending.</returns>
    public Task SettingsChanged(GroceryListSettingsResponse settings);
}
