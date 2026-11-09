using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>PATCH /api/grocery-list/items/{itemId}</c>: only the fields to change.
/// </summary>
/// <remarks>
/// <para>
/// A field that is absent (or <see langword="null"/>) keeps its current value, and an empty string clears an optional
/// field. Sending only what the user actually edited means two people editing different fields of the same item at
/// the same time both keep their change, where replacing the whole item would silently undo one of them.
/// </para>
/// <para>Validated automatically by the minimal API validation filter (<c>AddValidation</c>).</para>
/// </remarks>
public sealed class UpdateGroceryItemRequest
{
    /// <summary>Gets the new name. It can be changed but not cleared.</summary>
    [NotBlank(ErrorMessage = "Enter what to buy.")]
    [StringLength(GroceryItem.NameMaxLength, ErrorMessage = "Names can be at most {1} characters.")]
    [PlainText]
    public string? Name { get; init; }

    /// <summary>Gets the new quantity; empty clears it.</summary>
    [StringLength(GroceryItem.QuantityMaxLength, ErrorMessage = "Quantities can be at most {1} characters.")]
    [PlainText]
    public string? Quantity { get; init; }

    /// <summary>Gets the new brand; empty clears it.</summary>
    [StringLength(GroceryItem.BrandMaxLength, ErrorMessage = "Brands can be at most {1} characters.")]
    [PlainText]
    public string? Brand { get; init; }

    /// <summary>Gets the new store, one of the configured stores (<c>GroceryList:Stores</c>); empty clears it.</summary>
    [StringLength(GroceryItem.StoreMaxLength, ErrorMessage = "Store names can be at most {1} characters.")]
    [PlainText]
    public string? Store { get; init; }

    /// <summary>Gets the new notes; empty clears them.</summary>
    [StringLength(GroceryItem.NotesMaxLength, ErrorMessage = "Notes can be at most {1} characters.")]
    [PlainText(AllowLineBreaks = true)]
    public string? Notes { get; init; }
}
