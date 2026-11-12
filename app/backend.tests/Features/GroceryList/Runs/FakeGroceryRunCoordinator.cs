using System.Collections.Concurrent;
using LoginApp.Api.Features.GroceryList.Runs;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Stands in for Temporal in API tests: records the workflows the API starts and the signals it sends, and answers
/// with <see cref="NextResult"/>.
/// </summary>
internal sealed class FakeGroceryRunCoordinator : IGroceryRunCoordinator
{
    /// <summary>Gets the inputs of the workflows started, in order.</summary>
    public ConcurrentQueue<GroceryRunWorkflowInput> Started { get; } = new();

    /// <summary>Gets the signals sent, in order.</summary>
    public ConcurrentQueue<(GroceryRunSignalKind Signal, GroceryRunSignal Payload)> Signals { get; } = new();

    /// <summary>Gets or sets what the next calls answer.</summary>
    public GroceryRunCoordinatorResult NextResult { get; set; } = GroceryRunCoordinatorResult.Succeeded;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<GroceryRunCoordinatorResult> StartAsync(GroceryRunWorkflowInput input, CancellationToken cancellationToken)
    {
        if (NextResult == GroceryRunCoordinatorResult.Succeeded)
        {
            Started.Enqueue(input);
        }

        return Task.FromResult(NextResult);
    }

    /// <inheritdoc />
    public Task<GroceryRunCoordinatorResult> SignalAsync(
        GroceryRunSignalKind signal,
        GroceryRunSignal payload,
        CancellationToken cancellationToken)
    {
        if (NextResult == GroceryRunCoordinatorResult.Succeeded)
        {
            Signals.Enqueue((signal, payload));
        }

        return Task.FromResult(NextResult);
    }
}
