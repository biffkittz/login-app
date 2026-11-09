using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// EF Core mapping for <see cref="GroceryItem"/>.
/// </summary>
internal sealed class GroceryItemConfiguration : IEntityTypeConfiguration<GroceryItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GroceryItem> builder)
    {
        // The SPA generates IDs (see GroceryItem.Id), so the database must never generate one.
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Name).HasMaxLength(GroceryItem.NameMaxLength).IsRequired();
        builder.Property(item => item.Quantity).HasMaxLength(GroceryItem.QuantityMaxLength);
        builder.Property(item => item.Brand).HasMaxLength(GroceryItem.BrandMaxLength);
        builder.Property(item => item.Store).HasMaxLength(GroceryItem.StoreMaxLength);
        builder.Property(item => item.Notes).HasMaxLength(GroceryItem.NotesMaxLength);

        // Every UPDATE and DELETE checks that the row still has the version that was read, so a write that raced
        // another one fails instead of silently overwriting it.
        builder.Property(item => item.Version).IsConcurrencyToken();

        // Reading the list sorts by position. The ID breaks ties, so the order is identical for every reader.
        builder.HasIndex(item => new { item.Position, item.Id });

        // Deleting an account must not delete the family's groceries; the items just lose the name of their editor.
        builder.HasOne(item => item.LastModifiedBy)
            .WithMany()
            .HasForeignKey(item => item.LastModifiedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
