using LoginApp.Api.Features.GroceryList.Runs;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Builds the states the grocery run workflow publishes, with deadlines placed relative to the real clock, for tests
/// that put runs straight into the database (<see cref="Infrastructure.GroceryRunApiFactory.ApplyAsync"/>).
/// </summary>
internal static class RunStates
{
    private static readonly TimeSpan s_shoppingTimeLimit = TimeSpan.FromHours(4);

    /// <summary>A run in last call, opened a minute ago.</summary>
    /// <param name="shopperId">The shopper.</param>
    /// <param name="participantIds">The people asked.</param>
    /// <param name="endsIn">How long from now last call ends; negative for a deadline already past.</param>
    /// <param name="readyUserIds">Those of them who are ready.</param>
    /// <returns>The state, sequence 1.</returns>
    public static GroceryRunState LastCall(
        string shopperId,
        IReadOnlyList<string> participantIds,
        TimeSpan? endsIn = null,
        IReadOnlyList<string>? readyUserIds = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset endsAt = now + (endsIn ?? TimeSpan.FromMinutes(30));
        return new GroceryRunState(
            Guid.NewGuid(),
            Sequence: 1,
            GroceryRunStatus.LastCall,
            shopperId,
            participantIds,
            readyUserIds ?? [],
            StartedAt: now - TimeSpan.FromMinutes(1),
            endsAt,
            ShoppingStartedAt: null,
            EndedAt: null,
            ExpiresAt: endsAt + s_shoppingTimeLimit,
            LastCallOutcome: null);
    }

    /// <summary>The next state of <paramref name="run"/>: the shopping has begun.</summary>
    /// <param name="run">The run in last call.</param>
    /// <param name="startedAgo">How long ago the shopping began.</param>
    /// <returns>The state.</returns>
    public static GroceryRunState Shopping(GroceryRunState run, TimeSpan? startedAgo = null)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow - (startedAgo ?? TimeSpan.Zero);
        return run with
        {
            Sequence = run.Sequence + 1,
            Status = GroceryRunStatus.Shopping,
            ShoppingStartedAt = startedAt,
            ExpiresAt = startedAt + s_shoppingTimeLimit,
            LastCallOutcome = LastCallOutcome.TimeUp,
        };
    }

    /// <summary>The next state of <paramref name="run"/>: it has ended.</summary>
    /// <param name="run">The run.</param>
    /// <param name="status">How it ended.</param>
    /// <returns>The state.</returns>
    public static GroceryRunState Ended(GroceryRunState run, GroceryRunStatus status = GroceryRunStatus.Finished) =>
        run with { Sequence = run.Sequence + 1, Status = status, EndedAt = DateTimeOffset.UtcNow };
}
