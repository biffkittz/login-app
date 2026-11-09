using System.Threading.Channels;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Wakes <see cref="GroceryImageWorker"/> when an item starts waiting for a picture.
/// </summary>
/// <remarks>
/// The waiting items themselves are in the database (<see cref="GroceryItem.ImagePending"/>), so this carries no data
/// and several signals in a row count as one: the worker always looks for every waiting item once it wakes.
/// </remarks>
internal sealed class GroceryImageSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>Tells the worker there is something to do. Never blocks.</summary>
    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>
    /// Waits until <see cref="Notify"/> has been called since the last wait ended.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes when there is something to do.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken) => await _channel.Reader.ReadAsync(cancellationToken);
}
