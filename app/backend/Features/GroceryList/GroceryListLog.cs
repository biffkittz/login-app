namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Source-generated, allocation-free log messages for the grocery list.
/// </summary>
/// <remarks>
/// Messages identify users and items by opaque ID only. What a family buys is nobody else's business, so item
/// names, notes and the like are never logged.
/// </remarks>
internal static partial class GroceryListLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "User {UserId} added item {ItemId}")]
    public static partial void ItemAdded(ILogger logger, string userId, Guid itemId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "User {UserId} changed item {ItemId}")]
    public static partial void ItemUpdated(ILogger logger, string userId, Guid itemId);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "User {UserId} moved item {ItemId}")]
    public static partial void ItemMoved(ILogger logger, string userId, Guid itemId);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "User {UserId} deleted item {ItemId}")]
    public static partial void ItemDeleted(ILogger logger, string userId, Guid itemId);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information,
        Message = "Renumbered {ItemCount} item positions after a gap became too small to split")]
    public static partial void PositionsRenumbered(ILogger logger, int itemCount);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning,
        Message = "User {UserId} could not add an item: the list is full ({MaxItems} items)")]
    public static partial void ListFull(ILogger logger, string userId, int maxItems);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Warning,
        Message = "A write to item {ItemId} conflicted with a concurrent change and was not saved")]
    public static partial void ConcurrentChange(ILogger logger, Guid itemId);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information,
        Message = "User {UserId} turned AI pictures {State}")]
    public static partial void ImageGenerationChanged(ILogger logger, string userId, string state);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Debug, Message = "User {UserId} connected for realtime updates ({ConnectionId})")]
    public static partial void ClientConnected(ILogger logger, string userId, string connectionId);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Warning,
        Message = "Refused a realtime connection whose account no longer exists")]
    public static partial void ConnectionRefusedUnknownUser(ILogger logger);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Warning,
        Message = "Refused a realtime connection opened by a page on another site ({Origin})")]
    public static partial void ConnectionRefusedCrossOrigin(ILogger logger, string origin);
}
