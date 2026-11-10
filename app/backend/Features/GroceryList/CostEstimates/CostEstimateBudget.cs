using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Enforces <see cref="CostEstimateOptions.MaxEstimatesPerDay"/>: at most that many estimates are asked of Gemini in
/// any 24 hours, by all users together, whether or not they succeed.
/// </summary>
/// <remarks>
/// Like the picture budget, the count is kept in memory, so it starts again from zero when the app restarts, and each
/// instance (app and grocery) has its own. A user can't restart the app, and the app is one replica per instance.
/// </remarks>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The daily limit.</param>
internal sealed class CostEstimateBudget(TimeProvider timeProvider, IOptions<CostEstimateOptions> options)
{
    private static readonly TimeSpan s_window = TimeSpan.FromDays(1);

    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Lock _lock = new();

    /// <summary>Gets the daily limit.</summary>
    public int Limit => options.Value.MaxEstimatesPerDay;

    /// <summary>
    /// Uses up one estimate of today's allowance, if any is left.
    /// </summary>
    /// <returns><see langword="true"/> if the estimate may be asked for.</returns>
    public bool TryTake()
    {
        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            while (_recent.TryPeek(out DateTimeOffset oldest) && now - oldest >= s_window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= Limit)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
