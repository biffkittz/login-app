namespace LoginApp.Api.Infrastructure.Telemetry;

/// <summary>
/// Log messages about where metrics go (events 6000-6001). The OTLP password is never logged, nor the endpoint's path
/// or query: the host is enough to tell where metrics went.
/// </summary>
internal static partial class TelemetryLog
{
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "Metrics are sent to {OtlpHost} every {ExportIntervalSeconds} s as site {Site}.")]
    public static partial void Exporting(ILogger logger, string otlpHost, int exportIntervalSeconds, string site);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "Metrics aren't exported: Telemetry:OtlpEndpoint isn't set.")]
    public static partial void NotExporting(ILogger logger);
}
