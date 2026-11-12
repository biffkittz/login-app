namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Source-generated log messages for the connection to Temporal and the app's worker. Never logs the API key.
/// </summary>
internal static partial class TemporalLog
{
    [LoggerMessage(EventId = 7000, Level = LogLevel.Information,
        Message = "Temporal worker polling task queue {TaskQueue} in namespace {Namespace} at {Address}")]
    public static partial void WorkerStarted(ILogger logger, string taskQueue, string @namespace, string address);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
        Message = "The Temporal worker stopped; trying again in {Delay}. Grocery runs wait until it is back")]
    public static partial void WorkerFailed(ILogger logger, TimeSpan delay, Exception exception);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information,
        Message = "Temporal isn't configured (Temporal:Address), so grocery runs are unavailable")]
    public static partial void NotConfigured(ILogger logger);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "Temporal worker stopped")]
    public static partial void WorkerStopped(ILogger logger);
}
