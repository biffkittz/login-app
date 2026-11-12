using LoginApp.Api.Infrastructure.Temporal;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Starts and signals grocery run workflows on Temporal.
/// </summary>
/// <remarks>
/// <para>
/// Every run of a site has the same workflow ID, so Temporal itself guarantees that only one runs at a time: starting a
/// second while the first runs fails (<see cref="WorkflowAlreadyStartedException"/>). Once a run has ended its ID is
/// free again. The two sites share the namespace, so the ID includes the site's task queue.
/// </para>
/// <para>
/// Calls are made on behalf of a waiting phone, so each gets <see cref="s_callTimeout"/> at most; Temporal's client
/// retries transient failures within it.
/// </para>
/// </remarks>
/// <param name="client">The Temporal client (connected lazily).</param>
/// <param name="options">The site's task queue.</param>
/// <param name="logger">The logger.</param>
internal sealed class TemporalGroceryRunCoordinator(
    ITemporalClient client,
    IOptions<TemporalOptions> options,
    ILogger<TemporalGroceryRunCoordinator> logger) : IGroceryRunCoordinator
{
    private static readonly TimeSpan s_callTimeout = TimeSpan.FromSeconds(10);

    // Last call (4 hours at most) plus the shopping (a day at most), with room to spare. A workflow still running after
    // this is stuck (an activity failing over and over); Temporal ends it, and the run's deadlines end it in the app.
    private static readonly TimeSpan s_executionTimeout = TimeSpan.FromDays(2);

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>Gets the ID every run of this site has in Temporal.</summary>
    public string WorkflowId => WorkflowIdFor(options.Value.TaskQueue);

    /// <summary>Returns the ID every run of a site has in Temporal.</summary>
    /// <param name="taskQueue">The site's task queue.</param>
    /// <returns>The workflow ID.</returns>
    public static string WorkflowIdFor(string taskQueue) => $"{taskQueue}/grocery-run";

    /// <inheritdoc />
    public async Task<GroceryRunCoordinatorResult> StartAsync(GroceryRunWorkflowInput input, CancellationToken cancellationToken)
    {
        try
        {
            await client.StartWorkflowAsync(
                (GroceryRunWorkflow workflow) => workflow.RunAsync(input),
                new WorkflowOptions(WorkflowId, options.Value.TaskQueue)
                {
                    ExecutionTimeout = s_executionTimeout,
                    Rpc = new RpcOptions { CancellationToken = cancellationToken, Timeout = s_callTimeout },
                });
            return GroceryRunCoordinatorResult.Succeeded;
        }
        catch (WorkflowAlreadyStartedException)
        {
            return GroceryRunCoordinatorResult.AlreadyRunning;
        }
        catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
        {
            GroceryRunLog.TemporalUnreachable(logger, "start", exception);
            return GroceryRunCoordinatorResult.Unreachable;
        }
    }

    /// <inheritdoc />
    public async Task<GroceryRunCoordinatorResult> SignalAsync(
        GroceryRunSignalKind signal,
        GroceryRunSignal payload,
        CancellationToken cancellationToken)
    {
        WorkflowHandle<GroceryRunWorkflow> handle = client.GetWorkflowHandle<GroceryRunWorkflow>(WorkflowId);
        WorkflowSignalOptions signalOptions = new()
        {
            Rpc = new RpcOptions { CancellationToken = cancellationToken, Timeout = s_callTimeout },
        };
        try
        {
            await (signal switch
            {
                GroceryRunSignalKind.ImGood => handle.SignalAsync(workflow => workflow.ImGoodAsync(payload), signalOptions),
                GroceryRunSignalKind.StartShoppingNow => handle.SignalAsync(workflow => workflow.StartShoppingNowAsync(payload), signalOptions),
                GroceryRunSignalKind.Cancel => handle.SignalAsync(workflow => workflow.CancelAsync(payload), signalOptions),
                GroceryRunSignalKind.FinishShopping => handle.SignalAsync(workflow => workflow.FinishShoppingAsync(payload), signalOptions),
                _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Not a grocery run signal."),
            });
            return GroceryRunCoordinatorResult.Succeeded;
        }
        catch (RpcException exception) when (exception.Code == RpcException.StatusCode.NotFound)
        {
            // The workflow has completed (or never existed): there is no run to signal.
            return GroceryRunCoordinatorResult.NotRunning;
        }
        catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
        {
            GroceryRunLog.TemporalUnreachable(logger, "signal", exception);
            return GroceryRunCoordinatorResult.Unreachable;
        }
    }

    // A failed call (network, timeout, refused credentials) or a failed connection (the lazy client connects on its
    // first call). A cancelled request is the caller's business and isn't caught.
    private static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is RpcException or InvalidOperationException or TimeoutException or OperationCanceledException;
}
