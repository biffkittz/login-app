using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Images;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Reads and changes the shared grocery list, independent of HTTP concerns such as status codes. Every successful
/// write is broadcast to all connected clients.
/// </summary>
internal interface IGroceryListService
{
    /// <summary>
    /// Returns the whole list in order, with the stores and the list's settings.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every item, sorted by position.</returns>
    public Task<GroceryListResponse> GetListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns an item picture.
    /// </summary>
    /// <param name="imageId">The picture's ID.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The picture, or <see langword="null"/> if there is none with that ID.</returns>
    public Task<GroceryItemImage?> GetImageAsync(Guid imageId, CancellationToken cancellationToken);

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
    /// Moves an item to the top of the list or directly after another item, and changes its store when the request
    /// names one (dragged into another store's section).
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="itemId">The item to move.</param>
    /// <param name="request">Where to move it, and its new store.</param>
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

    /// <summary>
    /// Changes the list's settings present in <paramref name="request"/>, and tells every connected client.
    /// </summary>
    /// <param name="userId">The signed-in user making the change.</param>
    /// <param name="request">The settings to change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The settings as they are now, or why they weren't changed.</returns>
    public Task<GroceryListSettingsResult> UpdateSettingsAsync(
        string userId,
        UpdateGroceryListSettingsRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends an item's wait for its picture (<see cref="GroceryItem.ImagePending"/>): attaches the picture drawn for
    /// <paramref name="nameKey"/>, or none, and broadcasts the item. Called by <see cref="GroceryImageWorker"/>.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="nameKey">The name the picture was drawn for. If the item has been renamed since, it keeps waiting
    /// for its new name's picture.</param>
    /// <param name="imageId">The picture, or <see langword="null"/> when none could be drawn.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the item has been saved and broadcast, or left as it was.</returns>
    public Task FinishImageAsync(Guid itemId, string nameKey, Guid? imageId, CancellationToken cancellationToken);
}
