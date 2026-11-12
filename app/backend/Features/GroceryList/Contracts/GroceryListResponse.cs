namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>GET /api/grocery-list</c>: the whole list, the stores its items can be bought at, the list's in-app
/// settings, and the grocery run under way.
/// </summary>
/// <param name="Items">Every item, in list order.</param>
/// <param name="Stores">The stores an item's <c>store</c> may name, in the order the app groups the list by.</param>
/// <param name="Settings">The list's in-app settings.</param>
/// <param name="Run">The grocery run under way, or <see langword="null"/> when there is none.</param>
/// <param name="LastCallMinutes">The lengths of last call a new grocery run can have, in minutes, in the order to
/// offer them (<c>GroceryRuns:LastCallMinutes</c>).</param>
public sealed record GroceryListResponse(
    IReadOnlyList<GroceryItemResponse> Items,
    IReadOnlyList<string> Stores,
    GroceryListSettingsResponse Settings,
    GroceryRunResponse? Run,
    IReadOnlyList<int> LastCallMinutes);
