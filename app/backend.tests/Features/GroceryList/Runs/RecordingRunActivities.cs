using System.Threading.Channels;
using LoginApp.Api.Features.GroceryList.Runs;
using Temporalio.Activities;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Stands in for <see cref="GroceryRunActivities"/> in workflow tests: the same activity names, but each one only
/// records the state the workflow published, in order.
/// </summary>
internal sealed class RecordingRunActivities
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    private readonly Channel<(string Activity, GroceryRunState State)> _published = Channel.CreateUnbounded<(string, GroceryRunState)>();

    /// <summary>Records the opening of last call.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>A completed task.</returns>
    [Activity("OpenLastCall")]
    public Task OpenLastCallAsync(GroceryRunState state) => RecordAsync("OpenLastCall", state);

    /// <summary>Records who is ready.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>A completed task.</returns>
    [Activity("RecordReady")]
    public Task RecordReadyAsync(GroceryRunState state) => RecordAsync("RecordReady", state);

    /// <summary>Records the start of the shopping.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>A completed task.</returns>
    [Activity("StartShopping")]
    public Task StartShoppingAsync(GroceryRunState state) => RecordAsync("StartShopping", state);

    /// <summary>Records the end of the run.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>A completed task.</returns>
    [Activity("EndRun")]
    public Task EndRunAsync(GroceryRunState state) => RecordAsync("EndRun", state);

    /// <summary>
    /// Waits for the next published state, failing the test unless it came from <paramref name="activity"/> in time.
    /// </summary>
    /// <param name="activity">The activity expected to publish it.</param>
    /// <returns>The state.</returns>
    public async Task<GroceryRunState> NextAsync(string activity)
    {
        (string actual, GroceryRunState state) = await _published.Reader
            .ReadAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(activity, actual);
        return state;
    }

    /// <summary>Tells whether nothing has been published since the last <see cref="NextAsync"/>.</summary>
    /// <returns><see langword="true"/> when no state is waiting to be read.</returns>
    public bool NothingPublished() => !_published.Reader.TryPeek(out _);

    private Task RecordAsync(string activity, GroceryRunState state)
    {
        _published.Writer.TryWrite((activity, state));
        return Task.CompletedTask;
    }
}
