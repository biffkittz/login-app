using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Enforces <see cref="ImageGenerationOptions.MaxImagesPerDay"/>: at most that many pictures are requested from
/// Gemini in any 24 hours, whether or not they succeed.
/// </summary>
/// <remarks>
/// The count is kept in memory, so it starts again from zero when the app restarts. That is a deliberate trade:
/// a user can't restart the app, and counting in the database would mean comparing timestamps in a query, which the
/// SQLite database the tests use can't do.
/// </remarks>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The daily limit.</param>
internal sealed class GroceryImageBudget(TimeProvider timeProvider, IOptions<ImageGenerationOptions> options)
{
    private static readonly TimeSpan s_window = TimeSpan.FromDays(1);

    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Uses up one picture of today's allowance, if any is left.
    /// </summary>
    /// <returns><see langword="true"/> if the picture may be requested.</returns>
    public bool TryTake()
    {
        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            while (_recent.TryPeek(out DateTimeOffset oldest) && now - oldest >= s_window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= options.Value.MaxImagesPerDay)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
