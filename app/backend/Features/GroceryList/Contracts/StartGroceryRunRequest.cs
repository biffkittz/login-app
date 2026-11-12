namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>POST /api/grocery-list/run</c>.
/// </summary>
public sealed class StartGroceryRunRequest
{
    /// <summary>
    /// Gets how long last call lasts at most, in minutes: one of the lengths <c>GET /api/grocery-list</c> offers
    /// (<c>lastCallMinutes</c>).
    /// </summary>
    public int LastCallMinutes { get; init; }
}
