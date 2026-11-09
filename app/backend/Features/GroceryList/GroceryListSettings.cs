namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Settings of the shared list that its users change in the app (as opposed to <see cref="GroceryListOptions"/>,
/// which the operator sets in configuration). There is one list, so there is exactly one row.
/// </summary>
internal sealed class GroceryListSettings
{
    /// <summary>The ID of the only row.</summary>
    public const int SingletonId = 1;

    /// <summary>Gets or sets the row's ID, always <see cref="SingletonId"/>.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether new and renamed items get an AI-drawn picture. Turning it off stops new
    /// pictures only; items keep the pictures they have.
    /// </summary>
    public bool ImageGenerationEnabled { get; set; }

    /// <summary>
    /// Gets or sets a counter that increases with every change. Clients keep the settings with the highest version,
    /// as they do for items, and it is the optimistic-concurrency token.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Creates the settings a new list starts with. The migration inserts them, so this is only a fallback should the
    /// row ever be missing.
    /// </summary>
    /// <returns>New settings: pictures on.</returns>
    public static GroceryListSettings CreateDefault() =>
        new() { Id = SingletonId, ImageGenerationEnabled = true, Version = 1 };
}
