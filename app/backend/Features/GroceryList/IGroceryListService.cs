using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Reads and changes the shared grocery list, independent of HTTP concerns such as status codes. Every successful
/// write is broadcast to all connected clients.
/// </summary>
internal interface IGroceryListService
{
    /// <summary>
    /// Returns the whole list in order.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every item, sorted by position.</returns>
    public Task<GroceryListResponse> GetListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds an item, at the top of the list or after <see cref="CreateGroceryItemRequest.AfterItemId"/>.
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="request">The validated item details.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The change, or why the item wasn't added.</returns>
    public Task<GroceryListWriteResult> CreateItemAsync(
        string userId,
        CreateGroceryItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes the fields present in <paramref name="request"/> and leaves the others as they are.
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="itemId">The item to change.</param>
    /// <param name="request">The validated changes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The change, or why the item wasn't changed.</returns>
    public Task<GroceryListWriteResult> UpdateItemAsync(
        string userId,
        Guid itemId,
        UpdateGroceryItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves an item to the top of the list or directly after another item.
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="itemId">The item to move.</param>
    /// <param name="request">Where to move it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The change, or why the item wasn't moved.</returns>
    public Task<GroceryListWriteResult> MoveItemAsync(
        string userId,
        Guid itemId,
        MoveGroceryItemRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an item from the list.
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="itemId">The item to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The change, or why the item wasn't removed.</returns>
    public Task<GroceryListWriteResult> DeleteItemAsync(string userId, Guid itemId, CancellationToken cancellationToken);
}
