namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Whether the app's Temporal worker is polling, for the health check. Written by <see cref="TemporalWorkerService"/>
/// only; thread-safe.
/// </summary>
internal sealed class TemporalWorkerStatus
{
    private volatile State _state = new(false, "The Temporal worker hasn't started yet.");

    /// <summary>Gets a value indicating whether the worker is connected and polling its task queue.</summary>
    public bool IsRunning => _state.IsRunning;

    /// <summary>Gets why the worker isn't running, or what it is doing.</summary>
    public string Description => _state.Description;

    /// <summary>Records that the worker is connected and polling.</summary>
    /// <param name="taskQueue">The task queue it polls.</param>
    public void Running(string taskQueue) => _state = new(true, $"Polling task queue {taskQueue}.");

    /// <summary>Records that the worker isn't running, and why.</summary>
    /// <param name="reason">Why, without secrets.</param>
    public void Stopped(string reason) => _state = new(false, reason);

    // One reference, swapped whole, so a reader never sees a running flag with another state's description.
    private sealed record State(bool IsRunning, string Description);
}
