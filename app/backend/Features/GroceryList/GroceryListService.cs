using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The shared grocery list on top of EF Core. Each write takes its turn (<see cref="GroceryListWriteLock"/>), saves,
/// and then broadcasts what it changed through <see cref="IGroceryListNotifier"/>.
/// </summary>
/// <remarks>
/// Each write checks its cancellation token up to the moment the change is saved, never after: a saved change must
/// still be broadcast and logged even if its client has gone, or the other phones would disagree with the database
/// until they next reload the list.
/// </remarks>
/// <param name="dbContext">The application database.</param>
/// <param name="writeLock">Serializes writes, so decisions based on the current list stay valid until saved.</param>
/// <param name="notifier">Broadcasts saved changes.</param>
/// <param name="options">The list's limits.</param>
/// <param name="imageOptions">Whether pictures can be drawn at all (an API key is configured).</param>
/// <param name="imageSignal">Wakes the worker that draws pictures.</param>
/// <param name="timeProvider">The clock for creation and modification times.</param>
/// <param name="logger">The logger.</param>
internal sealed class GroceryListService(
    ApplicationDbContext dbContext,
    GroceryListWriteLock writeLock,
    IGroceryListNotifier notifier,
    IOptions<GroceryListOptions> options,
    IOptions<ImageGenerationOptions> imageOptions,
    GroceryImageSignal imageSignal,
    TimeProvider timeProvider,
    ILogger<GroceryListService> logger) : IGroceryListService
{
    /// <inheritdoc />
    public async Task<GroceryListResponse> GetListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<GroceryItemResponse> items = await ToResponses(dbContext.GroceryItems.AsNoTracking())
            .ToListAsync(cancellationToken);
        GroceryListSettings settings = await LoadSettingsAsync(track: false, cancellationToken);
        return new GroceryListResponse(items, options.Value.Stores, ToResponse(settings));
    }

    /// <inheritdoc />
    public Task<GroceryItemImage?> GetImageAsync(Guid imageId, CancellationToken cancellationToken) =>
        dbContext.GroceryItemImages.AsNoTracking().FirstOrDefaultAsync(image => image.Id == imageId, cancellationToken);

    /// <inheritdoc />
    public async Task<GroceryListWriteResult> CreateItemAsync(
        string userId,
        CreateGroceryItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveStore(request.Store, out string? store))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownStore);
        }

        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownUser);
        }

        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        List<GroceryItem> items = await LoadInOrderAsync(cancellationToken);

        int maxItems = options.Value.MaxItems;
        if (items.Count >= maxItems)
        {
            GroceryListLog.ListFull(logger, userId, maxItems);
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ListFull);
        }

        Guid itemId = request.Id is Guid requestedId && requestedId != Guid.Empty ? requestedId : Guid.CreateVersion7();
        if (items.Exists(item => item.Id == itemId))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.DuplicateItemId);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        GroceryItem newItem = new()
        {
            Id = itemId,
            Name = request.Name.Trim(),
            Quantity = Normalize(request.Quantity),
            Brand = Normalize(request.Brand),
            Store = store,
            Notes = Normalize(request.Notes),
            Version = 1,
            CreatedAt = now,
            LastModifiedAt = now,
            LastModifiedById = userId,
        };

        // A neighbor deleted since the client last looked is not worth refusing the item over (unlike a move, where
        // the user chose the spot deliberately): the item simply goes to the top. FindIndex's -1 means the same.
        int anchorIndex = request.AfterItemId is Guid afterItemId ? items.FindIndex(item => item.Id == afterItemId) : -1;
        List<GroceryItem> renumbered = Place(newItem, items, anchorIndex + 1);
        await PlanImageAsync(newItem, cancellationToken);

        dbContext.GroceryItems.Add(newItem);
        if (!await TrySaveAsync(itemId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ConcurrentChange);
        }

        GroceryListLog.ItemAdded(logger, userId, itemId);
        WakeImageWorkerIfWaiting(newItem);
        return await CompleteAsync([itemId, .. renumbered.Select(item => item.Id)], deletedItemIds: []);
    }

    /// <inheritdoc />
    public async Task<GroceryListWriteResult> UpdateItemAsync(
        string userId,
        Guid itemId,
        UpdateGroceryItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? store = null;
        if (request.Store is not null && !TryResolveStore(request.Store, out store))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownStore);
        }

        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownUser);
        }

        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        GroceryItem? item = await dbContext.GroceryItems.FirstOrDefaultAsync(existing => existing.Id == itemId, cancellationToken);
        if (item is null)
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ItemNotFound);
        }

        // Saving identical values would still bump the version and tell every phone about a change that isn't one.
        string previousNameKey = GroceryImageNames.KeyFor(item.Name);
        if (!ApplyChanges(item, request, store))
        {
            return await UnchangedAsync(itemId, cancellationToken);
        }

        // The picture shows what the name says, so a new name needs a new picture. Fixing the capitalization or the
        // spacing doesn't make it a new name.
        if (GroceryImageNames.KeyFor(item.Name) != previousNameKey)
        {
            await PlanImageAsync(item, cancellationToken);
        }

        item.Version++;
        item.LastModifiedAt = timeProvider.GetUtcNow();
        item.LastModifiedById = userId;
        if (!await TrySaveAsync(itemId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ConcurrentChange);
        }

        GroceryListLog.ItemUpdated(logger, userId, itemId);
        WakeImageWorkerIfWaiting(item);
        return await CompleteAsync([itemId], deletedItemIds: []);
    }

    /// <inheritdoc />
    public async Task<GroceryListWriteResult> MoveItemAsync(
        string userId,
        Guid itemId,
        MoveGroceryItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownUser);
        }

        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        List<GroceryItem> items = await LoadInOrderAsync(cancellationToken);

        int currentIndex = items.FindIndex(item => item.Id == itemId);
        if (currentIndex < 0)
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ItemNotFound);
        }

        GroceryItem movingItem = items[currentIndex];
        items.RemoveAt(currentIndex);

        int newIndex = 0;
        if (request.AfterItemId is Guid afterItemId)
        {
            int anchorIndex = items.FindIndex(item => item.Id == afterItemId);
            if (anchorIndex < 0)
            {
                // "After itself" finds nothing because the item was just taken out of the list; it means "stay".
                return afterItemId == itemId
                    ? await UnchangedAsync(itemId, cancellationToken)
                    : GroceryListWriteResult.Failure(GroceryListWriteStatus.AnchorNotFound);
            }

            newIndex = anchorIndex + 1;
        }

        if (newIndex == currentIndex)
        {
            return await UnchangedAsync(itemId, cancellationToken);
        }

        // A move changes the item's place, not its details, so the last-modified time and author stay as they are.
        // The version still goes up: that is what tells clients the new position is the newer copy.
        List<GroceryItem> renumbered = Place(movingItem, items, newIndex);
        movingItem.Version++;
        if (!await TrySaveAsync(itemId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ConcurrentChange);
        }

        GroceryListLog.ItemMoved(logger, userId, itemId);
        return await CompleteAsync([itemId, .. renumbered.Select(item => item.Id)], deletedItemIds: []);
    }

    /// <inheritdoc />
    public async Task<GroceryListWriteResult> DeleteItemAsync(string userId, Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.UnknownUser);
        }

        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        GroceryItem? item = await dbContext.GroceryItems.FirstOrDefaultAsync(existing => existing.Id == itemId, cancellationToken);
        if (item is null)
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ItemNotFound);
        }

        dbContext.GroceryItems.Remove(item);
        if (!await TrySaveAsync(itemId, cancellationToken))
        {
            return GroceryListWriteResult.Failure(GroceryListWriteStatus.ConcurrentChange);
        }

        GroceryListLog.ItemDeleted(logger, userId, itemId);
        return await CompleteAsync(savedItemIds: [], [itemId]);
    }

    /// <inheritdoc />
    public async Task<GroceryListSettingsResult> UpdateSettingsAsync(
        string userId,
        UpdateGroceryListSettingsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return GroceryListSettingsResult.Failure(GroceryListWriteStatus.UnknownUser);
        }

        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        GroceryListSettings settings = await LoadSettingsAsync(track: true, cancellationToken);
        if (request.ImageGenerationEnabled is not bool enabled || enabled == settings.ImageGenerationEnabled)
        {
            return GroceryListSettingsResult.Success(ToResponse(settings));
        }

        settings.ImageGenerationEnabled = enabled;
        settings.Version++;

        // Turning pictures off also stops the ones still waiting to be drawn, so their placeholders go now rather than
        // whenever the worker gets to them.
        List<GroceryItem> stopped = enabled
            ? []
            : await dbContext.GroceryItems.Where(item => item.ImagePending).ToListAsync(cancellationToken);
        foreach (GroceryItem item in stopped)
        {
            item.ImagePending = false;
            item.Version++;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return GroceryListSettingsResult.Failure(GroceryListWriteStatus.ConcurrentChange);
        }

        GroceryListLog.ImageGenerationChanged(logger, userId, enabled ? "on" : "off");
        GroceryListSettingsResponse response = ToResponse(settings);
        await notifier.SettingsChangedAsync(response);
        if (stopped.Count > 0)
        {
            await CompleteAsync([.. stopped.Select(item => item.Id)], deletedItemIds: []);
        }

        return GroceryListSettingsResult.Success(response);
    }

    /// <inheritdoc />
    public async Task FinishImageAsync(Guid itemId, string nameKey, Guid? imageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);
        GroceryItem? item = await dbContext.GroceryItems.FirstOrDefaultAsync(existing => existing.Id == itemId, cancellationToken);

        // Removed meanwhile; no longer waiting, because pictures were turned off; or renamed, so this picture would be
        // wrong and the new name's is still to come.
        if (item is null || !item.ImagePending || GroceryImageNames.KeyFor(item.Name) != nameKey)
        {
            return;
        }

        // Nobody edited the item, so its last-modified time and author stay. The version still goes up: that is what
        // makes every phone take the copy with the picture.
        item.ImageId = imageId;
        item.ImagePending = false;
        item.Version++;
        if (await TrySaveAsync(itemId, cancellationToken))
        {
            await CompleteAsync([itemId], deletedItemIds: []);
        }
    }

    private static IQueryable<GroceryItemResponse> ToResponses(IQueryable<GroceryItem> items) =>
        items
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .Select(item => new GroceryItemResponse(
                item.Id,
                item.Name,
                item.Quantity,
                item.Brand,
                item.Store,
                item.Notes,
                item.Position,
                item.Version,
                item.CreatedAt,
                item.LastModifiedAt,
                item.LastModifiedById,
                item.LastModifiedBy == null ? null : item.LastModifiedBy.DisplayName,
                item.ImageId == null ? null : GroceryImageUrls.For(item.ImageId.Value),
                item.ImagePending));

    // Applies the fields present in the request and reports whether any value actually changed. An absent field
    // keeps its value; an empty one clears an optional field (the name can't be empty: validation sees to that). The
    // store has already been resolved to its configured spelling (or null to clear it).
    private static bool ApplyChanges(GroceryItem item, UpdateGroceryItemRequest request, string? resolvedStore)
    {
        string name = request.Name?.Trim() ?? item.Name;
        string? quantity = request.Quantity is null ? item.Quantity : Normalize(request.Quantity);
        string? brand = request.Brand is null ? item.Brand : Normalize(request.Brand);
        string? store = request.Store is null ? item.Store : resolvedStore;
        string? notes = request.Notes is null ? item.Notes : Normalize(request.Notes);

        bool changed = name != item.Name
            || quantity != item.Quantity
            || brand != item.Brand
            || store != item.Store
            || notes != item.Notes;

        item.Name = name;
        item.Quantity = quantity;
        item.Brand = brand;
        item.Store = store;
        item.Notes = notes;
        return changed;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // A blank store means "none". Anything else must be one of the configured stores, and is stored with their
    // spelling, so "costco" and "Costco" land in the same group. Items saved before the list of stores existed keep
    // their store until someone changes it.
    private bool TryResolveStore(string? requested, out string? store)
    {
        store = string.IsNullOrWhiteSpace(requested) ? null : options.Value.FindStore(requested);
        return string.IsNullOrWhiteSpace(requested) || store is not null;
    }

    // Decides the picture of an item whose name has just been set, before it is saved: the picture already drawn for
    // that name, or a new one (the item waits, and the worker draws it after the save), or none while pictures are
    // off or the server has no API key.
    private async Task PlanImageAsync(GroceryItem item, CancellationToken cancellationToken)
    {
        item.ImageId = null;
        item.ImagePending = false;
        if (!imageOptions.Value.IsConfigured
            || !(await LoadSettingsAsync(track: false, cancellationToken)).ImageGenerationEnabled)
        {
            return;
        }

        item.ImageId = await dbContext.GroceryItemImages.FindIdByNameKeyAsync(GroceryImageNames.KeyFor(item.Name), cancellationToken);
        item.ImagePending = item.ImageId is null;
    }

    private void WakeImageWorkerIfWaiting(GroceryItem item)
    {
        if (item.ImagePending)
        {
            imageSignal.Notify();
        }
    }

    // The migration inserts the single row; should it ever be missing, the defaults stand in (and are saved with the
    // next change).
    private async Task<GroceryListSettings> LoadSettingsAsync(bool track, CancellationToken cancellationToken)
    {
        IQueryable<GroceryListSettings> query = track ? dbContext.GroceryListSettings : dbContext.GroceryListSettings.AsNoTracking();
        GroceryListSettings? settings = await query.FirstOrDefaultAsync(
            existing => existing.Id == GroceryListSettings.SingletonId,
            cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = GroceryListSettings.CreateDefault();
        if (track)
        {
            dbContext.GroceryListSettings.Add(settings);
        }

        return settings;
    }

    private GroceryListSettingsResponse ToResponse(GroceryListSettings settings) =>
        new(settings.Version, settings.ImageGenerationEnabled, imageOptions.Value.IsConfigured);

    private Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    private Task<List<GroceryItem>> LoadInOrderAsync(CancellationToken cancellationToken) =>
        dbContext.GroceryItems
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

    // Gives the item a position at `index` among `others` (the rest of the list, in order). Normally only the item
    // itself changes; when its neighbors are too close to fit between, the whole list is renumbered and the other
    // items that had to move are returned. `others` ends up holding the item when that happens.
    private List<GroceryItem> Place(GroceryItem item, List<GroceryItem> others, int index)
    {
        double? before = index > 0 ? others[index - 1].Position : null;
        double? after = index < others.Count ? others[index].Position : null;
        if (ItemPositions.Between(before, after) is double position)
        {
            item.Position = position;
            return [];
        }

        others.Insert(index, item);
        List<GroceryItem> renumbered = [];
        for (int i = 0; i < others.Count; i++)
        {
            GroceryItem current = others[i];
            double renumberedPosition = ItemPositions.AtIndex(i);
            if (ReferenceEquals(current, item))
            {
                item.Position = renumberedPosition;
            }
            else if (current.Position != renumberedPosition)
            {
                current.Position = renumberedPosition;
                current.Version++;
                renumbered.Add(current);
            }
        }

        GroceryListLog.PositionsRenumbered(logger, others.Count);
        return renumbered;
    }

    private async Task<bool> TrySaveAsync(Guid itemId, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            GroceryListLog.ConcurrentChange(logger, itemId);
            return false;
        }
    }

    // Nothing is saved or broadcast. The answer carries the item as it stands, so the client can settle its own
    // optimistic copy of it.
    private async Task<GroceryListWriteResult> UnchangedAsync(Guid itemId, CancellationToken cancellationToken)
    {
        List<GroceryItemResponse> items = await ToResponses(dbContext.GroceryItems.AsNoTracking().Where(item => item.Id == itemId))
            .ToListAsync(cancellationToken);
        return GroceryListWriteResult.Success(new GroceryListChange(items, DeletedItemIds: []));
    }

    // Runs once the change is saved, so it deliberately ignores the request's cancellation token (see the remarks
    // on this class).
    private async Task<GroceryListWriteResult> CompleteAsync(Guid[] savedItemIds, Guid[] deletedItemIds)
    {
        List<GroceryItemResponse> savedItems = savedItemIds.Length == 0
            ? []
            : await ToResponses(dbContext.GroceryItems.AsNoTracking().Where(item => savedItemIds.Contains(item.Id)))
                .ToListAsync(CancellationToken.None);

        GroceryListChange change = new(savedItems, deletedItemIds);
        await notifier.ListChangedAsync(change);
        return GroceryListWriteResult.Success(change);
    }
}
