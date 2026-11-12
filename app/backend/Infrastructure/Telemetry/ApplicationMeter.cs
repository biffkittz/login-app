namespace LoginApp.Api.Infrastructure.Telemetry;

/// <summary>
/// The app's own meter. Every custom instrument belongs to it, and the metrics pipeline subscribes to it next to
/// ASP.NET Core's and .NET's built-in meters (<see cref="TelemetryServiceCollectionExtensions"/>).
/// </summary>
/// <remarks>
/// Instruments are named <c>nibbles.&lt;area&gt;.&lt;what&gt;</c>. Prometheus stores them as
/// <c>nibbles_&lt;area&gt;_&lt;what&gt;</c>, plus <c>_total</c> for counters and a unit suffix such as
/// <c>_seconds</c>. The Grafana dashboards (<c>infra/modules/observability/dashboards</c>) query those names, so a
/// rename has to change them too. Each feature creates its instruments from
/// <see cref="System.Diagnostics.Metrics.IMeterFactory"/>, which hands every caller the same
/// <see cref="System.Diagnostics.Metrics.Meter"/> for this name.
/// </remarks>
internal static class ApplicationMeter
{
    /// <summary>The meter's name.</summary>
    public const string Name = "Nibbles";
}
