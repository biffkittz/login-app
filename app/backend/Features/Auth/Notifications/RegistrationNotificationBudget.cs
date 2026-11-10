using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Enforces <see cref="RegistrationNotificationOptions.MaxPerDay"/>: at most that many registration emails in any 24
/// hours, whether or not they reach anyone.
/// </summary>
/// <remarks>
/// The count is kept in memory, so it starts again from zero when the app restarts: nobody who registers can restart
/// the app, and the limit is there to stop a flood, not to count precisely.
/// </remarks>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The daily limit.</param>
internal sealed class RegistrationNotificationBudget(TimeProvider timeProvider, IOptions<RegistrationNotificationOptions> options)
{
    private static readonly TimeSpan s_window = TimeSpan.FromDays(1);

    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Uses up one email of today's allowance, if any is left.
    /// </summary>
    /// <param name="sentInWindow">How many emails, this one included, have been sent in the last 24 hours.</param>
    /// <returns><see langword="true"/> if the email may be sent.</returns>
    public bool TryTake(out int sentInWindow)
    {
        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            while (_recent.TryPeek(out DateTimeOffset oldest) && now - oldest >= s_window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= options.Value.MaxPerDay)
            {
                sentInWindow = _recent.Count;
                return false;
            }

            _recent.Enqueue(now);
            sentInWindow = _recent.Count;
            return true;
        }
    }
}
