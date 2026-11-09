namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// A grocery item as the API and the realtime hub send it.
/// </summary>
/// <param name="Id">The item's identifier.</param>
/// <param name="Name">What to buy.</param>
/// <param name="Quantity">How much to buy, or <see langword="null"/>.</param>
/// <param name="Brand">A preferred brand, or <see langword="null"/>.</param>
/// <param name="Store">The store to buy it at, or <see langword="null"/>.</param>
/// <param name="Notes">Free-text notes, or <see langword="null"/>.</param>
/// <param name="Position">The sort key: the list is ordered by ascending position.</param>
/// <param name="Version">Increases with every change. Clients keep the copy with the highest version.</param>
/// <param name="CreatedAt">When the item was added.</param>
/// <param name="LastModifiedAt">When the item's details last changed.</param>
/// <param name="LastModifiedById">Who added the item or last changed its details, or <see langword="null"/> if that
/// account no longer exists.</param>
/// <param name="LastModifiedByName">That user's display name, or <see langword="null"/>.</param>
public sealed record GroceryItemResponse(
    Guid Id,
    string Name,
    string? Quantity,
    string? Brand,
    string? Store,
    string? Notes,
    double Position,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    string? LastModifiedById,
    string? LastModifiedByName);
