using Temporalio.Activities;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The grocery run workflow's activities: each saves the state the workflow publishes and pushes the run to every
/// open list through the SignalR hub, which unlocks or locks the list on every phone.
/// </summary>
/// <remarks>
/// <para>
/// The four activities do the same thing; they have separate names so the workflow's history in the Temporal UI reads
/// as what happened (last call opened, someone was ready, the shopping started, the run ended).
/// </para>
/// <para>
/// Temporal may run an activity more than once (a retry after a timeout, two workers during a deployment) and late. A
/// state is saved only if it is newer than the database's (<see cref="GroceryRunStore.ApplyAsync"/>), and the run is
/// broadcast as the database has it, so repeats change nothing; phones keep the copy with the highest sequence.
/// </para>
/// <para>
/// Registered as one instance for the worker (<see cref="GroceryRunWorkerContribution"/>); each call gets its own
/// scope, and so its own database context.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope per call.</param>
/// <param name="notifier">Pushes the run to every open list.</param>
/// <param name="metrics">Counts runs by how they began and ended.</param>
/// <param name="logger">The logger.</param>
internal sealed class GroceryRunActivities(
    IServiceScopeFactory scopeFactory,
    IGroceryListNotifier notifier,
    GroceryRunMetrics metrics,
    ILogger<GroceryRunActivities> logger)
{
    /// <summary>Saves and broadcasts the run as last call opens: the list locks on every phone.</summary>
    /// <param name="state">The run's new state.</param>
    /// <returns>A task that completes when the state is saved and broadcast.</returns>
    [Activity("OpenLastCall")]
    public Task OpenLastCallAsync(GroceryRunState state) => PublishAsync(state);

    /// <summary>Saves and broadcasts who has said "I'm good" so far.</summary>
    /// <param name="state">The run's new state.</param>
    /// <returns>A task that completes when the state is saved and broadcast.</returns>
    [Activity("RecordReady")]
    public Task RecordReadyAsync(GroceryRunState state) => PublishAsync(state);

    /// <summary>Saves and broadcasts the end of last call: the list is final and unlocked, and the shopping begins.</summary>
    /// <param name="state">The run's new state.</param>
    /// <returns>A task that completes when the state is saved and broadcast.</returns>
    [Activity("StartShopping")]
    public Task StartShoppingAsync(GroceryRunState state) => PublishAsync(state);

    /// <summary>Saves and broadcasts the end of the run.</summary>
    /// <param name="state">The run's new state.</param>
    /// <returns>A task that completes when the state is saved and broadcast.</returns>
    [Activity("EndRun")]
    public Task EndRunAsync(GroceryRunState state) => PublishAsync(state);

    private async Task PublishAsync(GroceryRunState state)
    {
        CancellationToken cancellationToken = ActivityExecutionContext.Current.CancellationToken;
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        GroceryRunApplyResult result = await scope.ServiceProvider
            .GetRequiredService<GroceryRunStore>()
            .ApplyAsync(state, cancellationToken);

        // Saved: from here on, nothing is cancelled. A broadcast that fails fails the activity, whose retry finds the
        // state saved and broadcasts again.
        if (result.Applied)
        {
            Record(state, result);
        }

        await notifier.RunChangedAsync(result.Run);
    }

    private void Record(GroceryRunState state, GroceryRunApplyResult result)
    {
        foreach (Guid earlierRunId in result.SupersededRunIds)
        {
            GroceryRunLog.EarlierRunEnded(logger, state.RunId, earlierRunId);
            metrics.Ended(GroceryRunStatus.Cancelled);
        }

        if (result.PreviousStatus is null)
        {
            GroceryRunLog.LastCallOpened(logger, state.RunId);
            metrics.Started();
        }

        if (state.Status == GroceryRunStatus.Shopping && result.PreviousStatus != GroceryRunStatus.Shopping
            && state.LastCallOutcome is LastCallOutcome outcome)
        {
            GroceryRunLog.ShoppingStarted(logger, state.RunId, outcome);
            metrics.LastCallEnded(outcome);
        }

        if (state.Status is GroceryRunStatus.Finished or GroceryRunStatus.Expired or GroceryRunStatus.Cancelled
            && result.PreviousStatus is not (GroceryRunStatus.Finished or GroceryRunStatus.Expired or GroceryRunStatus.Cancelled))
        {
            GroceryRunLog.RunEnded(logger, state.RunId, state.Status);
            metrics.Ended(state.Status);
        }
    }
}
