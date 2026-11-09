namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>POST /api/grocery-list/items/{itemId}/move</c>.
/// </summary>
/// <remarks>
/// The new place is named by a neighbor rather than an index: "after Bread" still means what the user saw when
/// someone else has added or removed items in the meantime, whereas "position 3" might not.
/// </remarks>
public sealed class MoveGroceryItemRequest
{
    /// <summary>Gets the item to place it directly after, or <see langword="null"/> to move it to the top.</summary>
    public Guid? AfterItemId { get; init; }
}
