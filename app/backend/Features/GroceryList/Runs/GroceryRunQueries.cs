using Microsoft.EntityFrameworkCore;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Finds the grocery run under way, and tells whether it locks the list.
/// </summary>
/// <remarks>
/// Status decides in the database; times decide in memory. SQLite (the tests' database) can't compare
/// <see cref="DateTimeOffset"/> values in SQL, and there is rarely more than one run under way to filter anyway.
/// </remarks>
internal static class GroceryRunQueries
{
    /// <summary>
    /// How long past a deadline the app still believes a run's status. Normally the workflow moves on within a second
    /// of the deadline; if it doesn't (Temporal or the worker is down), the run lapses for the app after this.
    /// </summary>
    public static readonly TimeSpan DeadlineGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Finds the run under way: the latest one whose status says so and whose deadline hasn't passed.
    /// </summary>
    /// <param name="runs">The runs.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The run, untracked, or <see langword="null"/> when none is under way.</returns>
    public static async Task<GroceryRun?> FindCurrentAsync(this IQueryable<GroceryRun> runs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<GroceryRun> underWay = await runs
            .AsNoTracking()
            .Where(run => run.Status == GroceryRunStatus.LastCall || run.Status == GroceryRunStatus.Shopping)
            .ToListAsync(cancellationToken);
        return underWay.Where(run => now < run.ExpiresAt + DeadlineGrace).MaxBy(run => run.StartedAt);
    }

    /// <summary>
    /// Tells whether <paramref name="run"/> locks the list: it is in last call, and last call's deadline (plus
    /// <see cref="DeadlineGrace"/>) hasn't passed.
    /// </summary>
    /// <param name="run">The current run, or <see langword="null"/>.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> while items may be added and changed but not removed or moved.</returns>
    public static bool LocksList(this GroceryRun? run, DateTimeOffset now) =>
        run is { Status: GroceryRunStatus.LastCall } && now < run.LastCallEndsAt + DeadlineGrace;
}
