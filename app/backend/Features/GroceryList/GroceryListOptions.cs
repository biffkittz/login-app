using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Settings for the shared grocery list, bound from the <c>GroceryList</c> configuration section.
/// </summary>
internal sealed class GroceryListOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "GroceryList";

    /// <summary>
    /// Gets or sets how many items the list may hold. Anyone can register, so the cap keeps one account from
    /// filling the database, and keeps the list small enough to send whole to every phone.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxItems { get; set; } = 500;

    /// <summary>
    /// Gets or sets the stores an item can be bought at, in the order the app groups the list by. An item's store must
    /// be one of these, or none.
    /// </summary>
    /// <remarks>
    /// Set in appsettings.json rather than defaulted here: the configuration binder adds configured entries to a list's
    /// existing items instead of replacing them, so a default would end up duplicated.
    /// </remarks>
    [MinLength(1, ErrorMessage = "GroceryList:Stores must name at least one store.")]
    public List<string> Stores { get; set; } = [];

    /// <summary>
    /// Finds the configured store a user's choice refers to, ignoring case and surrounding spaces.
    /// </summary>
    /// <param name="name">The store name as sent by a client.</param>
    /// <returns>The store as configured, or <see langword="null"/> if it isn't one of <see cref="Stores"/>.</returns>
    public string? FindStore(string name) =>
        Stores.Find(store => string.Equals(store, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Stores.Exists(store => string.IsNullOrWhiteSpace(store) || store.Trim() != store || store.Length > GroceryItem.StoreMaxLength))
        {
            yield return new ValidationResult(
                $"GroceryList:Stores entries must be non-blank, without surrounding spaces, and at most {GroceryItem.StoreMaxLength} characters.",
                [nameof(Stores)]);
        }

        if (Stores.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Stores.Count)
        {
            yield return new ValidationResult("GroceryList:Stores must not name a store twice.", [nameof(Stores)]);
        }
    }
}
