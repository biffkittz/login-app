namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Where item pictures are served (<c>GET /api/grocery-list/images/{imageId}</c>, mapped by
/// <see cref="GroceryListEndpoints"/>).
/// </summary>
internal static class GroceryImageUrls
{
    /// <summary>
    /// Returns the URL of a picture. A picture's bytes never change (see <see cref="GroceryItemImage"/>), so its URL
    /// stands for exactly one picture and can be cached for good.
    /// </summary>
    /// <param name="imageId">The picture's ID.</param>
    /// <returns>The site-relative URL.</returns>
    public static string For(Guid imageId) => $"/api/grocery-list/images/{imageId}";
}
