using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// EF Core mapping for <see cref="GroceryListSettings"/>.
/// </summary>
internal sealed class GroceryListSettingsConfiguration : IEntityTypeConfiguration<GroceryListSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GroceryListSettings> builder)
    {
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.Version).IsConcurrencyToken();

        // The single row exists from the start, so reading the settings never has to create it.
        builder.HasData(GroceryListSettings.CreateDefault());
    }
}
