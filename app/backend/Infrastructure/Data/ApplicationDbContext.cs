using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Images;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// The EF Core context for the application database (Azure Database for PostgreSQL in the cloud, a local
/// PostgreSQL server during development).
/// </summary>
/// <remarks>
/// Derives from <see cref="IdentityUserContext{TUser}"/> rather than <c>IdentityDbContext</c>: the app has no
/// roles, so the role tables are intentionally left out of the schema. Adding roles later is an ordinary
/// migration (change the base class and run <c>dotnet ef migrations add</c>).
/// </remarks>
/// <param name="options">The options configured in <see cref="DatabaseServiceCollectionExtensions"/>.</param>
internal sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityUserContext<ApplicationUser>(options)
{
    /// <summary>Gets the items on the shared grocery list.</summary>
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();

    /// <summary>Gets the AI-drawn pictures of grocery items, one per item name.</summary>
    public DbSet<GroceryItemImage> GroceryItemImages => Set<GroceryItemImage>();

    /// <summary>Gets the list's in-app settings: a single row.</summary>
    public DbSet<GroceryListSettings> GroceryListSettings => Set<GroceryListSettings>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's mappings must be applied first; the configuration below refines them.
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ApplicationUserConfiguration());
        builder.ApplyConfiguration(new GroceryItemConfiguration());
        builder.ApplyConfiguration(new GroceryItemImageConfiguration());
        builder.ApplyConfiguration(new GroceryListSettingsConfiguration());
    }
}
