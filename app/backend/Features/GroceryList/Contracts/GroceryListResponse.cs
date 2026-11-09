namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>GET /api/grocery-list</c>: the whole list, and the stores its items can be bought at.
/// </summary>
/// <param name="Items">Every item, in list order.</param>
/// <param name="Stores">The stores an item's <c>store</c> may name, in the order the app groups the list by.</param>
public sealed record GroceryListResponse(IReadOnlyList<GroceryItemResponse> Items, IReadOnlyList<string> Stores);
