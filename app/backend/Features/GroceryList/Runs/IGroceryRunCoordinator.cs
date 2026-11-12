namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Starts grocery run workflows and signals them: the API's only contact with Temporal. Hides Temporal from
/// <see cref="GroceryRunService"/>, so the API's rules can be tested without a Temporal server.
/// </summary>
internal interface IGroceryRunCoordinator
{
    /// <summary>Gets a value indicating whether grocery runs are possible at all: Temporal is configured.</summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Starts a run's workflow. Its first activity creates the run in the database and pushes it to every open list.
    /// </summary>
    /// <param name="input">The run's input.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether it started.</returns>
    public Task<GroceryRunCoordinatorResult> StartAsync(GroceryRunWorkflowInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a signal to the site's run under way.
    /// </summary>
    /// <param name="signal">Which signal.</param>
    /// <param name="payload">Who sends it, for which run.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Whether it was delivered.</returns>
    public Task<GroceryRunCoordinatorResult> SignalAsync(GroceryRunSignalKind signal, GroceryRunSignal payload, CancellationToken cancellationToken);
}
