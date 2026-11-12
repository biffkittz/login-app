namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The outcome of a write to the grocery list.
/// </summary>
internal enum GroceryListWriteStatus
{
    /// <summary>The change was saved and broadcast.</summary>
    Succeeded,

    /// <summary>The token is genuine but its account no longer exists.</summary>
    UnknownUser,

    /// <summary>The item doesn't exist (any more).</summary>
    ItemNotFound,

    /// <summary>An item with the requested ID already exists.</summary>
    DuplicateItemId,

    /// <summary>The neighbor an item was to be moved next to doesn't exist (any more).</summary>
    AnchorNotFound,

    /// <summary>The list already holds <see cref="GroceryListOptions.MaxItems"/> items.</summary>
    ListFull,

    /// <summary>Another write changed the same item at the same moment.</summary>
    ConcurrentChange,

    /// <summary>The store isn't one of <see cref="GroceryListOptions.Stores"/>.</summary>
    UnknownStore,

    /// <summary>
    /// A grocery run's last call is on: items may be added and changed, but not removed or moved, until the shopping
    /// begins.
    /// </summary>
    LockedForLastCall,
}
