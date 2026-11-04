using LoginApp.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace LoginApp.Api.Infrastructure.Hosting;

/// <summary>
/// Health endpoints for Azure Container Apps probes and the Application Gateway backend health check.
/// </summary>
internal static class HealthCheckExtensions
{
    /// <summary>The liveness endpoint: is the process able to serve HTTP at all?</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>The readiness endpoint: can the app serve users (is the database reachable)?</summary>
    public const string ReadinessPath = "/health/ready";

    private const string ReadinessTag = "ready";

    /// <summary>
    /// Registers the health checks.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>(name: "database", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// Maps the liveness and readiness endpoints.
    /// </summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapApplicationHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no dependency checks: a database outage must not make the platform restart containers
        // that are themselves healthy, which would only add a restart loop to the outage.
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = static _ => false })
            .DisableHttpMetrics();

        // Readiness includes the database, so traffic is only routed to replicas that can actually serve it.
        endpoints.MapHealthChecks(
                ReadinessPath,
                new HealthCheckOptions { Predicate = static check => check.Tags.Contains(ReadinessTag) })
            .DisableHttpMetrics();

        return endpoints;
    }
}
