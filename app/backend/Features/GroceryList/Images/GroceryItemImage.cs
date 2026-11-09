namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// A small picture of a grocery item, drawn by Gemini from the item's name (see <see cref="GroceryImageWorker"/>).
/// </summary>
/// <remarks>
/// <para>
/// Pictures are shared by name: every item called "Milk" (in any letter case) gets the same one. Something the family
/// buys every week therefore costs one picture ever, and shows it the moment it's added again.
/// </para>
/// <para>
/// A picture never changes once saved; an item gets a different picture by pointing at another row. That is what lets
/// browsers cache <c>GET /api/grocery-list/images/{id}</c> for good. Rows stay when their items are deleted, so the
/// next item with that name can use them; they are a few kilobytes each, and the daily limit on new pictures
/// (<see cref="ImageGenerationOptions.MaxImagesPerDay"/>) bounds how quickly they accumulate.
/// </para>
/// </remarks>
internal sealed class GroceryItemImage
{
    /// <summary>Gets or sets the picture's identifier, which is also its URL and its ETag.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name the picture was drawn for, as <see cref="GroceryImageNames.KeyFor"/> normalizes it.
    /// Unique: one picture per name.
    /// </summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the encoded picture (see <see cref="GroceryImageThumbnails"/>).</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>Gets or sets the media type of <see cref="Content"/>, e.g. <c>image/webp</c>.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets when the picture was drawn.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
