namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Decides when two item names are the same for pictures (<see cref="GroceryItemImage.NameKey"/>).
/// </summary>
internal static class GroceryImageNames
{
    /// <summary>
    /// Normalizes an item name: trimmed, runs of whitespace as a single space, upper-cased (invariant culture). So
    /// "milk", "Milk " and "MILK" share one picture, and fixing the capitalization of a name keeps its picture.
    /// </summary>
    /// <param name="name">The item's name.</param>
    /// <returns>The key; never longer than <paramref name="name"/>.</returns>
    public static string KeyFor(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
