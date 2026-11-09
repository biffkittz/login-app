using LoginApp.Api.Infrastructure.Hosting;

namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// Refuses (403) any request, other than the health probes, whose connection presented no client certificate.
/// </summary>
/// <remarks>
/// <para>
/// Kestrel asks every HTTPS client for a certificate (<c>ClientCertificateMode.AllowCertificate</c>) and aborts
/// the handshake if one is presented that <see cref="ClientCertificateValidator"/> rejects. So a certificate on
/// the connection has already been validated, and this middleware only has to require that there is one.
/// </para>
/// <para>
/// The health endpoints are exempt because Container Apps' probes connect directly to the container and can't
/// present a certificate. They reveal only whether the app and its database are up.
/// </para>
/// </remarks>
internal sealed class ClientCertificateRequirementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClientCertificateRequirementMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientCertificateRequirementMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger.</param>
    public ClientCertificateRequirementMiddleware(RequestDelegate next, ILogger<ClientCertificateRequirementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Passes on requests that carry a client certificate or target a health endpoint, and answers everything else
    /// with 403 Forbidden.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Connection.ClientCertificate is not null || IsHealthProbe(context.Request.Path))
        {
            return _next(context);
        }

        OriginTlsLog.ClientCertificateMissing(_logger, context.Connection.RemoteIpAddress?.ToString() ?? "(unknown)");
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static bool IsHealthProbe(PathString path) =>
        path.Equals(HealthCheckExtensions.LivenessPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(HealthCheckExtensions.ReadinessPath, StringComparison.OrdinalIgnoreCase);
}
