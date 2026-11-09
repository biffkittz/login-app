namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Draws a picture of a grocery item from its name. Implemented by <see cref="GeminiImageGenerator"/>; tests swap in a
/// stand-in, so nothing in the test suite calls a paid API.
/// </summary>
internal interface IGroceryImageGenerator
{
    /// <summary>
    /// Asks the image model for a picture of <paramref name="itemName"/>.
    /// </summary>
    /// <param name="itemName">The item's name, as the user typed it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The picture as the model encoded it (PNG, typically 1024 pixels square).</returns>
    /// <exception cref="ImageGenerationException">The model drew nothing, or the API refused the request.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached.</exception>
    public Task<byte[]> GenerateAsync(string itemName, CancellationToken cancellationToken);
}
