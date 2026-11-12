namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Source-generated log messages for grocery runs. Users and runs by opaque ID only, as everywhere in the app.
/// </summary>
internal static partial class GroceryRunLog
{
    [LoggerMessage(EventId = 5040, Level = LogLevel.Information,
        Message = "User {UserId} started grocery run {RunId} (last call {LastCallMinutes} minutes; people asked: {ParticipantCount})")]
    public static partial void RunStarted(ILogger logger, string userId, Guid runId, int lastCallMinutes, int participantCount);

    [LoggerMessage(EventId = 5041, Level = LogLevel.Information, Message = "User {UserId} sent {Signal} to grocery run {RunId}")]
    public static partial void SignalSent(ILogger logger, string userId, GroceryRunSignalKind signal, Guid runId);

    [LoggerMessage(EventId = 5042, Level = LogLevel.Information, Message = "Grocery run {RunId} opened last call")]
    public static partial void LastCallOpened(ILogger logger, Guid runId);

    [LoggerMessage(EventId = 5043, Level = LogLevel.Information, Message = "Grocery run {RunId}: last call ended ({Outcome}), shopping began")]
    public static partial void ShoppingStarted(ILogger logger, Guid runId, LastCallOutcome outcome);

    [LoggerMessage(EventId = 5044, Level = LogLevel.Information, Message = "Grocery run {RunId} ended ({Status})")]
    public static partial void RunEnded(ILogger logger, Guid runId, GroceryRunStatus status);

    [LoggerMessage(EventId = 5045, Level = LogLevel.Warning,
        Message = "Grocery run {RunId} ended earlier run {EarlierRunId}, which was still marked as under way")]
    public static partial void EarlierRunEnded(ILogger logger, Guid runId, Guid earlierRunId);

    [LoggerMessage(EventId = 5046, Level = LogLevel.Warning, Message = "Couldn't reach Temporal to {Operation} a grocery run")]
    public static partial void TemporalUnreachable(ILogger logger, string operation, Exception exception);

    [LoggerMessage(EventId = 5047, Level = LogLevel.Information,
        Message = "User {UserId} couldn't start a grocery run: one is already under way")]
    public static partial void AlreadyUnderWay(ILogger logger, string userId);
}
