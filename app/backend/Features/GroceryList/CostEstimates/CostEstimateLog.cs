namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Source-generated log messages for cost estimates.
/// </summary>
/// <remarks>
/// Like <see cref="GroceryListLog"/>, these name users by ID and count items; they never include item names, prices or
/// the model's text. Failure reasons come from <see cref="CostEstimateException"/> messages, which leave those out too.
/// </remarks>
internal static partial class CostEstimateLog
{
    [LoggerMessage(EventId = 5030, Level = LogLevel.Information,
        Message = "User {UserId} got a cost estimate for {ItemCount} items ({EstimatedItemCount} priced) in {ElapsedMilliseconds} ms")]
    public static partial void Estimated(ILogger logger, string userId, int itemCount, int estimatedItemCount, long elapsedMilliseconds);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Warning, Message = "Couldn't estimate the list's cost for user {UserId}: {Reason}")]
    public static partial void Failed(ILogger logger, string userId, string reason);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Warning,
        Message = "Refused a cost estimate for user {UserId}: the limit of {MaxEstimatesPerDay} estimates a day has been reached")]
    public static partial void DailyLimitReached(ILogger logger, string userId, int maxEstimatesPerDay);

    [LoggerMessage(EventId = 5033, Level = LogLevel.Warning,
        Message = "Gemini's cost estimate for user {UserId} answered for none of the list's {ItemCount} items")]
    public static partial void NothingMatched(ILogger logger, string userId, int itemCount);
}
