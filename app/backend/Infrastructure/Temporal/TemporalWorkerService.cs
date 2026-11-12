using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Worker;

namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Runs the app's Temporal worker inside this process: it polls <see cref="TemporalOptions.TaskQueue"/> and executes
/// the workflows and activities the features contribute (<see cref="ITemporalWorkerContribution"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why in this process.</b> Activities push their results to phones through the SignalR hub, which lives here and
/// has no backplane; a worker elsewhere couldn't reach it. A family's workflows are a few tasks an hour, so the worker
/// costs nothing noticeable, and there is nothing extra to deploy or pay for.
/// </para>
/// <para>
/// <b>Why not the SDK's hosted worker.</b> <c>Temporalio.Extensions.Hosting</c> connects once and lets a failure escape
/// <c>ExecuteAsync</c>, which stops the whole app (BackgroundService's default). Temporal is needed for grocery runs
/// only, so this service keeps trying instead, with growing pauses, while the list goes on working. Workflows lose
/// nothing meanwhile: their state is in Temporal, and their timers and signals wait for a worker.
/// </para>
/// <para>
/// <b>Deployments.</b> A new revision starts before the old one stops, so for a moment two workers poll the same queue
/// and either may take a task. Activities are written for that: each applies a numbered state, so applying it twice,
/// or late, changes nothing.
/// </para>
/// </remarks>
/// <param name="client">The client, connected lazily; the same one the API uses to start and signal workflows.</param>
/// <param name="contributions">The features' workflows and activities.</param>
/// <param name="options">The task queue and connection settings.</param>
/// <param name="status">Where the worker reports whether it is running, for the health check.</param>
/// <param name="loggerFactory">Creates the worker's loggers.</param>
/// <param name="timeProvider">The clock, for pauses between attempts.</param>
/// <param name="logger">The logger.</param>
internal sealed class TemporalWorkerService(
    ITemporalClient client,
    IEnumerable<ITemporalWorkerContribution> contributions,
    IOptions<TemporalOptions> options,
    TemporalWorkerStatus status,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider,
    ILogger<TemporalWorkerService> logger) : BackgroundService
{
    // Pauses after consecutive failures: quick at first (a blip, a deployment of the namespace), then every few
    // minutes (a revoked key or a deleted namespace, which only an operator can fix), so the log stays readable.
    private static readonly TimeSpan[] s_retryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
    ];

    // A worker that ran this long before failing was healthy; its next failure starts the pauses from the beginning.
    private static readonly TimeSpan s_healthyRun = TimeSpan.FromMinutes(5);

    // On shutdown, activities in flight get this long to finish before they are cancelled; a cancelled one is retried
    // by the next worker. Well within the host's 30-second shutdown timeout.
    private static readonly TimeSpan s_gracefulShutdown = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TemporalOptions settings = options.Value;
        int failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            long startedAt = timeProvider.GetTimestamp();
            try
            {
                // A lazy client connects on first use, but a worker can't be its first user: connect explicitly. The
                // SDK's connect takes no token, so stop waiting for it when the app stops.
                await client.Connection.ConnectAsync().WaitAsync(stoppingToken);

                TemporalWorkerOptions workerOptions = new(settings.TaskQueue)
                {
                    LoggerFactory = loggerFactory,
                    GracefulShutdownTimeout = s_gracefulShutdown,
                };
                foreach (ITemporalWorkerContribution contribution in contributions)
                {
                    contribution.AddTo(workerOptions);
                }

                using TemporalWorker worker = new(client, workerOptions);
                status.Running(settings.TaskQueue);
                TemporalLog.WorkerStarted(logger, settings.TaskQueue, settings.Namespace ?? string.Empty, settings.Address ?? string.Empty);

                // Returns only when stopping or after a fatal failure (a worker never "completes").
                await worker.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failures = timeProvider.GetElapsedTime(startedAt) >= s_healthyRun ? 1 : failures + 1;
                TimeSpan delay = s_retryDelays[Math.Min(failures, s_retryDelays.Length) - 1];
                status.Stopped($"The worker failed ({exception.GetType().Name}); retrying in {delay}.");
                TemporalLog.WorkerFailed(logger, delay, exception);
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        status.Stopped("The app is shutting down.");
        TemporalLog.WorkerStopped(logger);
    }
}
