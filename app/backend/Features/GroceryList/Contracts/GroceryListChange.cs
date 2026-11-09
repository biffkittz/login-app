namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// What one write did to the list. It is both the HTTP response to that write and the message the realtime hub
/// sends every connected client, so the SPA applies its own changes and everybody else's with the same code.
/// </summary>
/// <param name="SavedItems">Items created or changed, including items whose position changed because the list was
/// renumbered (see <c>ItemPositions</c>).</param>
/// <param name="DeletedItemIds">Items removed from the list.</param>
public sealed record GroceryListChange(IReadOnlyList<GroceryItemResponse> SavedItems, IReadOnlyList<Guid> DeletedItemIds);
