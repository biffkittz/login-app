using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace LoginApp.Api.Infrastructure.Telemetry;

/// <summary>
/// Registers the app's metrics: the health check gauge, and, when <see cref="TelemetryOptions.OtlpEndpoint"/> is set,
/// the OpenTelemetry pipeline that pushes the app's own and the platform's metrics to Prometheus.
/// </summary>
internal static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// The service name every metric is sent with. Prometheus turns it into the <c>job</c> label; the replica's host
    /// name becomes <c>instance</c>, and <see cref="TelemetryOptions.Site"/> the <c>site</c> label.
    /// </summary>
    public const string ServiceName = "nibbles";

    /// <summary>The resource attribute that carries <see cref="TelemetryOptions.Site"/>.</summary>
    public const string SiteAttribute = "site";

    // Prometheus' OTLP receiver answers within milliseconds inside the environment; a slow answer only delays the next
    // export, which then carries the latest values anyway.
    private static readonly TimeSpan s_exportTimeout = TimeSpan.FromSeconds(10);

    // ASP.NET Core's and .NET's own meters that are worth the series they cost. Others, such as MsQuic's internals
    // and routing match attempts, are left out.
    private static readonly string[] s_builtInMeters =
    [
        "Microsoft.AspNetCore.Hosting", // HTTP requests by route: rate, status codes, duration
        "Microsoft.AspNetCore.Server.Kestrel", // connections, TLS handshakes, upgraded (WebSocket) connections
        "Microsoft.AspNetCore.Http.Connections", // SignalR connections and how long they last
        "Microsoft.AspNetCore.Diagnostics", // exceptions the exception handler caught
        "Microsoft.AspNetCore.RateLimiting", // requests the rate limits let through or refused
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.AspNetCore.Identity", // accounts created, sign-ins, password checks
        "Microsoft.EntityFrameworkCore", // queries, saves, concurrency conflicts
        "Npgsql", // the PostgreSQL connection pool and commands
        "System.Net.Http", // calls to Gemini and Cloudflare
        "System.Runtime", // CPU, memory, garbage collection, thread pool
    ];

    /// <summary>
    /// Adds the telemetry options, the health check gauge and, when configured, the OTLP metrics exporter.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The app's configuration, complete with Key Vault.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplicationTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
            .BindConfiguration(TelemetryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<HealthCheckMetrics>();
        services.AddSingleton<IHealthCheckPublisher>(static serviceProvider => serviceProvider.GetRequiredService<HealthCheckMetrics>());

        // Whether there is an exporter at all must be known now, while services are registered, so the section is read
        // directly. The same values are validated at startup (ValidateOnStart above), so a bad one still stops the app
        // before anything is sent.
        TelemetryOptions options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
        if (!options.IsExporting)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                // The replica's host name tells apart two replicas of a revision and an old and a new revision while
                // they overlap; each must send its own series, or Prometheus would mix two processes' counters.
                resource.AddService(
                    ServiceName,
                    serviceVersion: typeof(TelemetryServiceCollectionExtensions).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    autoGenerateServiceInstanceId: false,
                    serviceInstanceId: Environment.MachineName);
                if (!string.IsNullOrWhiteSpace(options.Site))
                {
                    resource.AddAttributes([new KeyValuePair<string, object>(SiteAttribute, options.Site)]);
                }
            })
            .WithMetrics(metrics => metrics
                .AddMeter([ApplicationMeter.Name, .. s_builtInMeters])
                .AddOtlpExporter((exporter, reader) =>
                {
                    // Prometheus' OTLP receiver takes protobuf over HTTP, not gRPC. A full URL set here is used as it is
                    // (only the OTEL_EXPORTER_OTLP_ENDPOINT variable gets /v1/metrics appended).
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Endpoint = options.OtlpEndpoint!;
                    exporter.TimeoutMilliseconds = (int)s_exportTimeout.TotalMilliseconds;
                    exporter.HttpClientFactory = () => CreateExportClient(options);

                    // Prometheus stores cumulative series; it can't take deltas without an experimental feature.
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.ExportIntervalSeconds * 1000;
                }));

        return services;
    }

    /// <summary>
    /// Logs where metrics go, once at startup.
    /// </summary>
    /// <param name="app">The app.</param>
    public static void LogMetricsExport(this WebApplication app)
    {
        TelemetryOptions options = app.Services.GetRequiredService<IOptions<TelemetryOptions>>().Value;
        if (options.OtlpEndpoint is { } endpoint)
        {
            TelemetryLog.Exporting(app.Logger, endpoint.Host, options.ExportIntervalSeconds, options.Site ?? Environment.MachineName);
        }
        else
        {
            TelemetryLog.NotExporting(app.Logger);
        }
    }

    // The exporter keeps one client for the app's lifetime. Recycling its connections every few minutes picks up a
    // changed address behind the endpoint's name; the credentials go in a header of their own rather than through
    // the exporter's comma-separated header string, which would have to escape them.
    private static HttpClient CreateExportClient(TelemetryOptions options)
    {
        HttpClient client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: true)
        {
            Timeout = s_exportTimeout,
        };
        if (!string.IsNullOrEmpty(options.OtlpUsername))
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.OtlpUsername}:{options.OtlpPassword}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }
}
