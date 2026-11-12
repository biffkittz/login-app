using System.Diagnostics.Metrics;
using LoginApp.Api.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace LoginApp.Api.Tests.Infrastructure.Telemetry;

/// <summary>
/// The health check gauge: one series per check, valued by the latest report.
/// </summary>
public sealed class HealthCheckMetricsTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

    private IMeterFactory MeterFactory => _services.GetRequiredService<IMeterFactory>();

    [Fact]
    public void BeforeTheFirstReport_TheGaugeHasNoValue()
    {
        // Arrange
        _ = new HealthCheckMetrics(MeterFactory);
        using MetricCollector<double> status = new(MeterFactory, ApplicationMeter.Name, "nibbles.health.status");

        // Act
        status.RecordObservableInstruments();

        // Assert: "no data" rather than a made-up healthy or unhealthy value.
        Assert.Empty(status.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task EachCheck_ReportsItsLatestStatus()
    {
        // Arrange
        HealthCheckMetrics metrics = new(MeterFactory);
        using MetricCollector<double> status = new(MeterFactory, ApplicationMeter.Name, "nibbles.health.status");
        await metrics.PublishAsync(Report(("database", HealthStatus.Healthy), ("cache", HealthStatus.Healthy)), TestContext.Current.CancellationToken);

        // Act: the database is lost, the cache struggles.
        await metrics.PublishAsync(Report(("database", HealthStatus.Unhealthy), ("cache", HealthStatus.Degraded)), TestContext.Current.CancellationToken);
        status.RecordObservableInstruments();

        // Assert
        var values = status.GetMeasurementSnapshot()
            .ToDictionary(measurement => (string)measurement.Tags[HealthCheckMetrics.CheckTag]!, measurement => measurement.Value);
        Assert.Equal(new Dictionary<string, double> { ["database"] = 0, ["cache"] = 0.5 }, values);
    }

    [Fact]
    public void Statuses_MapToOneHalfAndZero()
    {
        Assert.Equal(1, HealthCheckMetrics.ToValue(HealthStatus.Healthy));
        Assert.Equal(0.5, HealthCheckMetrics.ToValue(HealthStatus.Degraded));
        Assert.Equal(0, HealthCheckMetrics.ToValue(HealthStatus.Unhealthy));
    }

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    private static HealthReport Report(params (string Name, HealthStatus Status)[] checks) =>
        new(
            checks.ToDictionary(
                check => check.Name,
                check => new HealthReportEntry(check.Status, description: null, TimeSpan.Zero, exception: null, data: null)),
            TimeSpan.Zero);
}
