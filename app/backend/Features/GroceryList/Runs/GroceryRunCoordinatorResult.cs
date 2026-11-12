namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// What became of a call to Temporal through <see cref="IGroceryRunCoordinator"/>.
/// </summary>
internal enum GroceryRunCoordinatorResult
{
    /// <summary>The workflow started, or got the signal.</summary>
    Succeeded,

    /// <summary>A run's workflow is already running (when starting).</summary>
    AlreadyRunning,

    /// <summary>No run's workflow is running any more (when signalling).</summary>
    NotRunning,

    /// <summary>Temporal couldn't be reached, or refused the app's credentials.</summary>
    Unreachable,
}
