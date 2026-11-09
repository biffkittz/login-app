using Microsoft.EntityFrameworkCore;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Queries shared by the list service, which gives a new item a picture drawn earlier, and the worker, which checks
/// for one before drawing.
/// </summary>
internal static class GroceryImageQueries
{
    /// <summary>
    /// Finds the picture drawn for a name.
    /// </summary>
    /// <param name="images">The pictures.</param>
    /// <param name="nameKey">The name, normalized by <see cref="GroceryImageNames.KeyFor"/>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The picture's ID, or <see langword="null"/> if none has been drawn for the name yet.</returns>
    public static Task<Guid?> FindIdByNameKeyAsync(
        this IQueryable<GroceryItemImage> images,
        string nameKey,
        CancellationToken cancellationToken) =>
        images
            .Where(image => image.NameKey == nameKey)
            .Select(image => (Guid?)image.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
