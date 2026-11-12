using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Extensions.DiagnosticSource;
using Temporalio.Runtime;

namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Registers the connection to Temporal and the app's worker, when <see cref="TemporalOptions.Address"/> is set.
/// </summary>
internal static class TemporalServiceCollectionExtensions
{
    /// <summary>
    /// The meter that carries the Temporal SDK's own metrics (worker slots, polls, task latencies and failures), named
    /// <c>temporal_*</c> by the SDK. The OTLP pipeline exports it with the app's meters.
    /// </summary>
    public const string MeterName = "Temporal";

    /// <summary>
    /// Adds <see cref="TemporalOptions"/> and, when Temporal is configured, a lazily connected
    /// <see cref="ITemporalClient"/>, the worker and its health check. Features contribute workflows and activities
    /// through <see cref="ITemporalWorkerContribution"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The app's configuration, complete with Key Vault.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTemporal(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TemporalOptions>()
            .BindConfiguration(TemporalOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Whether there is a worker at all must be known while services are registered, so the section is read
        // directly here; the same values are validated at startup (above).
        TemporalOptions options = configuration.GetSection(TemporalOptions.SectionName).Get<TemporalOptions>() ?? new();
        if (!options.IsConfigured)
        {
            return services;
        }

        // One runtime for the process (the SDK's Rust core: its threads and its metrics). Its metrics go to a .NET
        // meter, so they leave with the app's own over OTLP, with durations in seconds as Prometheus expects.
        services.AddSingleton(static serviceProvider =>
        {
            Meter meter = serviceProvider.GetRequiredService<IMeterFactory>().Create(MeterName);
            ILogger coreLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Temporalio.Core");
            return new TemporalRuntime(new TemporalRuntimeOptions
            {
                Telemetry = new TelemetryOptions
                {
                    Logging = new LoggingOptions
                    {
                        Filter = new TelemetryFilterOptions(TelemetryFilterOptions.Level.Warn, TelemetryFilterOptions.Level.Warn),
                        Forwarding = new LogForwardingOptions(coreLogger),
                    },
                    Metrics = new MetricsOptions
                    {
                        CustomMetricMeter = new CustomMetricMeter(meter),
                        CustomMetricMeterOptions = new CustomMetricMeterOptions
                        {
                            HistogramDurationFormat = CustomMetricMeterOptions.DurationFormat.FloatSeconds,
                        },

                        // Every series would carry service_name="temporal-core-sdk"; the OTLP resource says who sent it.
                        AttachServiceName = false,
                    },
                },
            });
        });

        // Lazy: building the app, and starting it, never wait for Temporal. The first call connects, and a failed
        // connection is tried again on the next call. The worker shares this client and its connection.
        services.AddSingleton<ITemporalClient>(static serviceProvider =>
        {
            TemporalOptions settings = serviceProvider.GetRequiredService<IOptions<TemporalOptions>>().Value;
            return TemporalClient.CreateLazy(new TemporalClientConnectOptions(settings.Address!)
            {
                Namespace = settings.Namespace!,
                ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : settings.ApiKey,
                Runtime = serviceProvider.GetRequiredService<TemporalRuntime>(),
                LoggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>(),
            });
        });

        services.AddSingleton<TemporalWorkerStatus>();
        services.AddHostedService<TemporalWorkerService>();
        services.AddHealthChecks().AddCheck<TemporalWorkerHealthCheck>(TemporalWorkerHealthCheck.Name);
        return services;
    }

    /// <summary>
    /// Logs, once at startup, that Temporal isn't configured, so a missing setting in Azure is easy to spot.
    /// </summary>
    /// <param name="app">The built app.</param>
    /// <returns>The same app, for chaining.</returns>
    public static WebApplication LogTemporalConfiguration(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<TemporalOptions>>().Value.IsConfigured)
        {
            TemporalLog.NotConfigured(app.Logger);
        }

        return app;
    }
}
