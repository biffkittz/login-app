using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// EF Core mapping for <see cref="GroceryItemImage"/>.
/// </summary>
internal sealed class GroceryItemImageConfiguration : IEntityTypeConfiguration<GroceryItemImage>
{
    /// <summary>The longest media type stored in <see cref="GroceryItemImage.ContentType"/>.</summary>
    public const int ContentTypeMaxLength = 50;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GroceryItemImage> builder)
    {
        builder.Property(image => image.Id).ValueGeneratedNever();

        // A key is the item name trimmed, with single spaces and upper-cased, so it is never longer than the name.
        builder.Property(image => image.NameKey).HasMaxLength(GroceryItem.NameMaxLength).IsRequired();
        builder.HasIndex(image => image.NameKey).IsUnique();

        builder.Property(image => image.Content).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(ContentTypeMaxLength).IsRequired();
    }
}
