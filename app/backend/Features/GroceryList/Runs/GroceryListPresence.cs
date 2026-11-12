namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Who has the list open right now: the users with at least one realtime connection that
/// <see cref="GroceryListHub"/> accepted. A grocery run asks them, and only them, during last call.
/// </summary>
/// <remarks>
/// In memory, like the hub itself: the app is a single instance (SignalR has no backplane). During a deployment the
/// old and new revisions each know only their own connections, for the few seconds before phones move to the new one.
/// Thread-safe.
/// </remarks>
internal sealed class GroceryListPresence
{
    private readonly Dictionary<string, int> _connectionsByUser = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Records an accepted connection of <paramref name="userId"/>.</summary>
    /// <param name="userId">The connected user.</param>
    public void Connected(string userId)
    {
        lock (_lock)
        {
            _connectionsByUser[userId] = _connectionsByUser.GetValueOrDefault(userId) + 1;
        }
    }

    /// <summary>Records that one of <paramref name="userId"/>'s accepted connections closed.</summary>
    /// <param name="userId">The user.</param>
    public void Disconnected(string userId)
    {
        lock (_lock)
        {
            if (_connectionsByUser.TryGetValue(userId, out int connections))
            {
                if (connections <= 1)
                {
                    _connectionsByUser.Remove(userId);
                }
                else
                {
                    _connectionsByUser[userId] = connections - 1;
                }
            }
        }
    }

    /// <summary>Returns the users who have the list open, in a fixed (ordinal) order.</summary>
    /// <returns>Their IDs.</returns>
    public IReadOnlyList<string> OnlineUserIds()
    {
        lock (_lock)
        {
            return [.. _connectionsByUser.Keys.Order(StringComparer.Ordinal)];
        }
    }
}
