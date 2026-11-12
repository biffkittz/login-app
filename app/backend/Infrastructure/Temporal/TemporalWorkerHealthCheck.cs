using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Reports whether the Temporal worker is polling. At worst <see cref="HealthStatus.Degraded"/>, never unhealthy:
/// without Temporal only grocery runs wait, and the list itself works. It isn't part of readiness either, so a Temporal
/// outage never takes the site out of service. The health gauge (<c>nibbles.health.status</c>) shows it in Grafana.
/// </summary>
/// <param name="status">The worker's status.</param>
internal sealed class TemporalWorkerHealthCheck(TemporalWorkerStatus status) : IHealthCheck
{
    /// <summary>The check's name, and the <c>check</c> label of its gauge.</summary>
    public const string Name = "temporal";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(status.IsRunning
            ? HealthCheckResult.Healthy(status.Description)
            : HealthCheckResult.Degraded(status.Description));
}
