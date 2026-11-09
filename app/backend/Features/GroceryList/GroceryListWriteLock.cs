namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Serializes writes to the grocery list within this process.
/// </summary>
/// <remarks>
/// <para>
/// A write reads the list (to count it, or to find an item's new neighbors) and then changes it. Run concurrently,
/// two such writes could both see room for one more item, or give two items the same position. Taking turns keeps
/// those decisions consistent. A family's list sees a handful of writes per minute, so waiting is never noticeable.
/// </para>
/// <para>
/// An in-process lock is enough because the app runs as a single instance: realtime updates (SignalR without a
/// backplane) need that anyway. If instances ever overlap, for example during a deployment, the per-item version
/// check still stops a write from silently overwriting another.
/// </para>
/// </remarks>
internal sealed class GroceryListWriteLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// Waits for this caller's turn to write.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A lease that ends the turn when disposed.</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        // Releasing twice would let two writers in at once, so a second Dispose does nothing.
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
