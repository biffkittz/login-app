using LoginApp.Api.Infrastructure.Data;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// One entry on the family's shared grocery list.
/// </summary>
/// <remarks>
/// There is a single list that every signed-in user shares, so items belong to no list or owner; who touched an item
/// last is recorded in <see cref="LastModifiedById"/> instead.
/// </remarks>
internal sealed class GroceryItem
{
    /// <summary>The maximum length of <see cref="Name"/>, shared by request validation and the database schema.</summary>
    public const int NameMaxLength = 100;

    /// <summary>The maximum length of <see cref="Quantity"/>.</summary>
    public const int QuantityMaxLength = 50;

    /// <summary>The maximum length of <see cref="Brand"/>.</summary>
    public const int BrandMaxLength = 100;

    /// <summary>The maximum length of <see cref="Store"/>.</summary>
    public const int StoreMaxLength = 100;

    /// <summary>The maximum length of <see cref="Notes"/>.</summary>
    public const int NotesMaxLength = 1000;

    /// <summary>
    /// Gets or sets the item's identifier. The SPA usually chooses it, so it can show a new item before the server
    /// has answered and recognize that item again when the server's copy arrives.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets what to buy, e.g. "Milk". Always present.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets how much to buy, as free text ("2", "1 dozen", "500 g").</summary>
    public string? Quantity { get; set; }

    /// <summary>Gets or sets a preferred brand.</summary>
    public string? Brand { get; set; }

    /// <summary>Gets or sets the store to buy it at.</summary>
    public string? Store { get; set; }

    /// <summary>Gets or sets free-text notes ("the lactose-free one").</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the sort key: the list is ordered by ascending position. Moving an item only changes its own
    /// position (to a value between its new neighbors), so a move touches one row; see <see cref="ItemPositions"/>.
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// Gets or sets a counter that increases with every change to the item, including moves. Clients keep the copy
    /// with the highest version, which makes realtime updates safe to apply in any order. It is also the
    /// optimistic-concurrency token, so two writers can never silently overwrite each other's change.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Gets or sets when the item was added.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the item's details last changed. Moving an item does not count as a change.</summary>
    public DateTimeOffset LastModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user who added the item or last changed its details; <see langword="null"/> once
    /// that account no longer exists.
    /// </summary>
    public string? LastModifiedById { get; set; }

    /// <summary>Gets or sets the user identified by <see cref="LastModifiedById"/>.</summary>
    public ApplicationUser? LastModifiedBy { get; set; }
}
