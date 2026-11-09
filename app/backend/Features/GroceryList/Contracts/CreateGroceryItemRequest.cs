using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>POST /api/grocery-list/items</c>.
/// </summary>
/// <remarks>
/// Validated automatically by the minimal API validation filter (<c>AddValidation</c>). Values are trimmed when
/// saved, and empty optional values are stored as "not set".
/// </remarks>
public sealed class CreateGroceryItemRequest
{
    /// <summary>
    /// Gets the ID for the new item. The SPA generates one, so it can show the item at once and recognize the
    /// server's copy when it arrives; the server generates one when it is omitted.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>Gets what to buy.</summary>
    [Required(ErrorMessage = "Enter what to buy.")]
    [StringLength(GroceryItem.NameMaxLength, ErrorMessage = "Names can be at most {1} characters.")]
    [PlainText]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets how much to buy, as free text.</summary>
    [StringLength(GroceryItem.QuantityMaxLength, ErrorMessage = "Quantities can be at most {1} characters.")]
    [PlainText]
    public string? Quantity { get; init; }

    /// <summary>Gets a preferred brand.</summary>
    [StringLength(GroceryItem.BrandMaxLength, ErrorMessage = "Brands can be at most {1} characters.")]
    [PlainText]
    public string? Brand { get; init; }

    /// <summary>Gets the store to buy it at: one of the configured stores (<c>GroceryList:Stores</c>), or none.</summary>
    [StringLength(GroceryItem.StoreMaxLength, ErrorMessage = "Store names can be at most {1} characters.")]
    [PlainText]
    public string? Store { get; init; }

    /// <summary>Gets free-text notes.</summary>
    [StringLength(GroceryItem.NotesMaxLength, ErrorMessage = "Notes can be at most {1} characters.")]
    [PlainText(AllowLineBreaks = true)]
    public string? Notes { get; init; }

    /// <summary>
    /// Gets the item the new one should follow. <see langword="null"/>, or an item that no longer exists, puts it at
    /// the top of the list. "Undo" uses it to put a deleted item back where it was.
    /// </summary>
    public Guid? AfterItemId { get; init; }
}
