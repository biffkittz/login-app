using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LoginApp.Api.Infrastructure.Telemetry;

/// <summary>
/// Reports the app's health checks as the gauge <c>nibbles.health.status</c>: 1 while a check is healthy, 0.5 while
/// it is degraded and 0 while it fails, one series per check (the <c>check</c> attribute, e.g. <c>database</c>).
/// </summary>
/// <remarks>
/// The health check service runs every check periodically (by default 5 seconds after start, then every 30 seconds)
/// and hands the report to this publisher; the gauge shows the latest one. Unlike the platform's probes, which only
/// see the moment they ask, the history then shows when and for how long the database was out of reach.
/// </remarks>
internal sealed class HealthCheckMetrics : IHealthCheckPublisher
{
    /// <summary>The attribute naming the health check.</summary>
    public const string CheckTag = "check";

    private HealthReport? _latest;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public HealthCheckMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        meter.CreateObservableGauge(
            "nibbles.health.status",
            Observe,
            unit: null,
            description: "Latest result of each health check: 1 healthy, 0.5 degraded, 0 unhealthy.");
    }

    /// <inheritdoc />
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _latest, report);
        return Task.CompletedTask;
    }

    /// <summary>Converts a health status into the gauge's value.</summary>
    /// <param name="status">The status.</param>
    /// <returns>1, 0.5 or 0.</returns>
    internal static double ToValue(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => 1,
        HealthStatus.Degraded => 0.5,
        _ => 0,
    };

    private IEnumerable<Measurement<double>> Observe()
    {
        HealthReport? report = Volatile.Read(ref _latest);
        if (report is null)
        {
            return [];
        }

        return report.Entries.Select(entry => new Measurement<double>(
            ToValue(entry.Value.Status),
            new KeyValuePair<string, object?>(CheckTag, entry.Key)));
    }
}
