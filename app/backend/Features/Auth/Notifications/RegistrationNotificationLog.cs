namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Source-generated log messages for registration emails.
/// </summary>
/// <remarks>
/// Counts and reasons only: never an address, a name or the text of an email. The reasons come from
/// <see cref="Infrastructure.Email.EmailDeliveryException"/>, whose messages are written to be logged.
/// </remarks>
internal static partial class RegistrationNotificationLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Registration emails are on: each registration is emailed to {RecipientCount} recipients")]
    public static partial void On(ILogger logger, int recipientCount);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Registration emails are off: RegistrationNotifications:Recipients is empty")]
    public static partial void OffWithoutRecipients(ILogger logger);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "Registration emails are off: RegistrationNotifications:Recipients is set, but email isn't (Email:AccountId, Email:ApiToken and Email:SenderAddress)")]
    public static partial void OffWithoutEmail(ILogger logger);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Sent the registration email to {RecipientCount} recipients: {DeliveredCount} delivered, {QueuedCount} queued")]
    public static partial void Sent(ILogger logger, int recipientCount, int deliveredCount, int queuedCount);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Cloudflare couldn't deliver the registration email to {Count} recipients: the address bounced or is suppressed")]
    public static partial void Undeliverable(ILogger logger, int count);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = "Couldn't send the registration email (attempt {Attempt}), trying again: {Reason}")]
    public static partial void Retrying(ILogger logger, int attempt, string reason);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning, Message = "Couldn't send the registration email: {Reason}")]
    public static partial void Failed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Error, Message = "Sending the registration email failed unexpectedly")]
    public static partial void FailedUnexpectedly(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Warning,
        Message = "Registration not emailed: {MaxPerDay} registration emails have gone out in the last 24 hours, the most RegistrationNotifications:MaxPerDay allows")]
    public static partial void DailyLimitReached(ILogger logger, int maxPerDay);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Warning, Message = "Registration not emailed: {Capacity} registration emails are already waiting to be sent")]
    public static partial void Dropped(ILogger logger, int capacity);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning, Message = "{Count} registration emails weren't sent before the app stopped")]
    public static partial void LostAtShutdown(ILogger logger, int count);
}
