namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// Source-generated, allocation-free log messages for database startup tasks.
/// </summary>
internal static partial class DatabaseLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Applying pending database migrations")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Database schema is up to date")]
    public static partial void MigrationsApplied(ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Skipping database migrations because Database:MigrateOnStartup is false")]
    public static partial void MigrationsSkipped(ILogger logger);
}
