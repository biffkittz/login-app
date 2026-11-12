namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Stands in for <see cref="TemporalGroceryRunCoordinator"/> when Temporal isn't configured: grocery runs are
/// unavailable, and the app says so.
/// </summary>
internal sealed class UnavailableGroceryRunCoordinator : IGroceryRunCoordinator
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<GroceryRunCoordinatorResult> StartAsync(GroceryRunWorkflowInput input, CancellationToken cancellationToken) =>
        Task.FromResult(GroceryRunCoordinatorResult.Unreachable);

    /// <inheritdoc />
    public Task<GroceryRunCoordinatorResult> SignalAsync(
        GroceryRunSignalKind signal,
        GroceryRunSignal payload,
        CancellationToken cancellationToken) =>
        Task.FromResult(GroceryRunCoordinatorResult.Unreachable);
}
