namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Source-generated log messages for drawing item pictures.
/// </summary>
/// <remarks>
/// Like <see cref="GroceryListLog"/>, these name items by ID only, never by name. Failure reasons come from
/// <see cref="ImageGenerationException"/> messages and HTTP errors, which never include the name either.
/// </remarks>
internal static partial class GroceryImageLog
{
    [LoggerMessage(EventId = 5020, Level = LogLevel.Information,
        Message = "Drew a picture for item {ItemId}: {Bytes} bytes, in {ElapsedMilliseconds} ms")]
    public static partial void ImageDrawn(ILogger logger, Guid itemId, int bytes, long elapsedMilliseconds);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Debug,
        Message = "Item {ItemId} reuses the picture drawn earlier for the same name")]
    public static partial void ImageReused(ILogger logger, Guid itemId);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Warning, Message = "Couldn't draw a picture for item {ItemId}: {Reason}")]
    public static partial void ImageFailed(ILogger logger, Guid itemId, string reason);

    [LoggerMessage(EventId = 5023, Level = LogLevel.Warning, Message = "Couldn't draw a picture for item {ItemId}")]
    public static partial void ImageFailedUnexpectedly(ILogger logger, Guid itemId, Exception exception);

    [LoggerMessage(EventId = 5024, Level = LogLevel.Information,
        Message = "Asking again for the picture of item {ItemId} in {Delay} after a temporary failure: {Reason}")]
    public static partial void ImageRetrying(ILogger logger, Guid itemId, TimeSpan delay, string reason);

    [LoggerMessage(EventId = 5025, Level = LogLevel.Warning,
        Message = "No picture for item {ItemId}: the limit of {MaxImagesPerDay} pictures a day has been reached")]
    public static partial void DailyLimitReached(ILogger logger, Guid itemId, int maxImagesPerDay);

    [LoggerMessage(EventId = 5026, Level = LogLevel.Error, Message = "Drawing item pictures failed; trying again in {Delay}")]
    public static partial void WorkerFailed(ILogger logger, TimeSpan delay, Exception exception);

    [LoggerMessage(EventId = 5027, Level = LogLevel.Information,
        Message = "AI pictures are unavailable: no Gemini API key is configured (ImageGeneration:ApiKey)")]
    public static partial void NotConfigured(ILogger logger);
}
