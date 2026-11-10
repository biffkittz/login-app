using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>POST /api/grocery-list/items/{itemId}/move</c>.
/// </summary>
/// <remarks>
/// <para>
/// The new place is named by a neighbor rather than an index: "after Bread" still means what the user saw when
/// someone else has added or removed items in the meantime, whereas "position 3" might not.
/// </para>
/// <para>
/// Dragging an item into another store's section moves it and changes its store in one step. Doing both in one write
/// means every phone sees it arrive in its new section at once, and a refused store can't leave the item moved to a
/// place that only made sense in the other section.
/// </para>
/// <para>Validated automatically by the minimal API validation filter (<c>AddValidation</c>).</para>
/// </remarks>
public sealed class MoveGroceryItemRequest
{
    /// <summary>Gets the item to place it directly after, or <see langword="null"/> to move it to the top.</summary>
    public Guid? AfterItemId { get; init; }

    /// <summary>
    /// Gets the item's new store, one of the configured stores (<c>GroceryList:Stores</c>); empty clears it, and
    /// <see langword="null"/> (or absent) keeps the store it has.
    /// </summary>
    [StringLength(GroceryItem.StoreMaxLength, ErrorMessage = "Store names can be at most {1} characters.")]
    [PlainText]
    public string? Store { get; init; }
}
