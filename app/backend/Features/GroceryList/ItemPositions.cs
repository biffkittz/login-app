namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Computes <see cref="GroceryItem.Position"/> values ("fractional indexing").
/// </summary>
/// <remarks>
/// <para>
/// The list is ordered by a floating-point position. An item moved between two neighbors gets the midpoint of their
/// positions, so a move rewrites one row and produces one small realtime update, instead of renumbering every item
/// in between.
/// </para>
/// <para>
/// Halving a gap cannot go on forever: after about 30 moves into the same spot the gap falls below
/// <see cref="MinimumGap"/>. <see cref="Between"/> then reports that there is no room, and the caller renumbers the
/// whole list with <see cref="AtIndex"/>. That is rare, and cheap for a list capped at a few hundred items.
/// </para>
/// </remarks>
internal static class ItemPositions
{
    /// <summary>The distance between neighbors after renumbering, and when adding at either end of the list.</summary>
    public const double Spacing = 1024;

    /// <summary>The smallest gap still split in two. Smaller gaps trigger renumbering.</summary>
    public const double MinimumGap = 1e-6;

    // Far below where a double's precision would come near MinimumGap (about 4e9). Each item added at the top moves
    // the smallest position down by Spacing, so only millions of additions without a renumbering could get here.
    private const double MaximumMagnitude = 1e9;

    /// <summary>
    /// Returns a position that sorts between two neighbors.
    /// </summary>
    /// <param name="before">The position of the item that will precede it, or <see langword="null"/> at the top.</param>
    /// <param name="after">The position of the item that will follow it, or <see langword="null"/> at the bottom.</param>
    /// <returns>The new position, or <see langword="null"/> when there is no usable room and the list must be
    /// renumbered first.</returns>
    public static double? Between(double? before, double? after)
    {
        double? position = (before, after) switch
        {
            (null, null) => 0,
            (null, double next) => next - Spacing,
            (double previous, null) => previous + Spacing,
            (double previous, double next) when next - previous > MinimumGap => previous + ((next - previous) / 2),
            _ => null,
        };

        return position is double value && Math.Abs(value) < MaximumMagnitude ? value : null;
    }

    /// <summary>
    /// Returns the evenly spaced position for the item at <paramref name="index"/> of a renumbered list.
    /// </summary>
    /// <param name="index">The zero-based index in the list.</param>
    /// <returns>The position.</returns>
    public static double AtIndex(int index) => index * Spacing;
}
