using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The database's copy of the grocery runs: written by the workflow's activities (<see cref="ApplyAsync"/>), read by the
/// API and the list's write rules.
/// </summary>
/// <param name="dbContext">The application database.</param>
/// <param name="writeLock">The list's write lock. Applying a state takes its turn like any write, so a write that has
/// checked the lock can't be overtaken by last call before it saves.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class GroceryRunStore(ApplicationDbContext dbContext, GroceryListWriteLock writeLock, TimeProvider timeProvider)
{
    /// <summary>
    /// Saves a state the workflow published, unless the database already has it or a newer one. A run that is new to
    /// the database ends any other run still marked as under way.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancels the operation until the state is saved.</param>
    /// <returns>The run as it now stands, and what this call changed.</returns>
    /// <exception cref="DbUpdateException">Another process saved the same state at the same moment (during a
    /// deployment); the activity's retry finds it saved.</exception>
    public async Task<GroceryRunApplyResult> ApplyAsync(GroceryRunState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable turn = await writeLock.AcquireAsync(cancellationToken);

        GroceryRun? run = await dbContext.GroceryRuns
            .Include(existing => existing.Participants)
            .FirstOrDefaultAsync(existing => existing.Id == state.RunId, cancellationToken);
        GroceryRunStatus? previousStatus = run?.Status;
        List<Guid> supersededRunIds = [];
        if (run is null)
        {
            supersededRunIds = await EndEarlierRunsAsync(state, cancellationToken);
            run = await CreateAsync(state, cancellationToken);
        }

        bool applied = state.Sequence > run.Sequence;
        if (applied)
        {
            run.Sequence = state.Sequence;
            run.Status = state.Status;
            run.LastCallEndsAt = state.LastCallEndsAt;
            run.ShoppingStartedAt = state.ShoppingStartedAt;
            run.EndedAt = state.EndedAt;
            run.ExpiresAt = state.ExpiresAt;
            run.LastCallOutcome = state.LastCallOutcome;
            foreach (GroceryRunParticipant participant in run.Participants)
            {
                participant.IsReady = state.ReadyUserIds.Contains(participant.UserId, StringComparer.Ordinal);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Saved, so no more cancelling: the caller must still broadcast the run.
        GroceryRunResponse response = (await FindResponseAsync(run.Id, CancellationToken.None))!;
        return new GroceryRunApplyResult(response, applied, previousStatus, supersededRunIds);
    }

    /// <summary>
    /// Returns the run under way, as clients see it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The run, or <see langword="null"/> when none is under way.</returns>
    public async Task<GroceryRunResponse?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        GroceryRun? run = await dbContext.GroceryRuns.FindCurrentAsync(timeProvider.GetUtcNow(), cancellationToken);
        return run is null ? null : await FindResponseAsync(run.Id, cancellationToken);
    }

    /// <summary>
    /// Returns the run under way, as stored.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The run, untracked and with its participants, or <see langword="null"/> when none is under way.</returns>
    public async Task<GroceryRun?> FindCurrentRunAsync(CancellationToken cancellationToken)
    {
        GroceryRun? run = await dbContext.GroceryRuns.FindCurrentAsync(timeProvider.GetUtcNow(), cancellationToken);
        return run is null
            ? null
            : await dbContext.GroceryRuns.AsNoTracking().Include(found => found.Participants).FirstOrDefaultAsync(found => found.Id == run.Id, cancellationToken);
    }

    /// <summary>
    /// Tells whether last call locks the list right now. Call it during the caller's turn on
    /// <see cref="GroceryListWriteLock"/>, which <see cref="ApplyAsync"/> takes too, so the answer holds until the
    /// caller has saved.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> while items may be added and changed but not removed or moved.</returns>
    public async Task<bool> IsListLockedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return (await dbContext.GroceryRuns.FindCurrentAsync(now, cancellationToken)).LocksList(now);
    }

    private Task<GroceryRunResponse?> FindResponseAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.GroceryRuns
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new GroceryRunResponse(
                run.Id,
                run.Sequence,
                run.Status,
                run.ShopperId,
                run.Shopper == null ? null : run.Shopper.DisplayName,
                run.StartedAt,
                run.LastCallEndsAt,
                run.ShoppingStartedAt,
                run.EndedAt,
                run.LastCallOutcome,
                run.Participants
                    .OrderBy(participant => participant.User!.DisplayName)
                    .ThenBy(participant => participant.UserId)
                    .Select(participant => new GroceryRunParticipantResponse(participant.UserId, participant.User!.DisplayName, participant.IsReady))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    // Only one run can be under way: Temporal allows one workflow per ID. A run still marked as under way when a new one
    // starts is one whose workflow ended without saying so (terminated by an operator, say); it ends now.
    private async Task<List<Guid>> EndEarlierRunsAsync(GroceryRunState state, CancellationToken cancellationToken)
    {
        List<GroceryRun> earlier = await dbContext.GroceryRuns
            .Where(run => run.Id != state.RunId && (run.Status == GroceryRunStatus.LastCall || run.Status == GroceryRunStatus.Shopping))
            .ToListAsync(cancellationToken);
        foreach (GroceryRun run in earlier)
        {
            run.Status = GroceryRunStatus.Cancelled;
            run.EndedAt ??= state.StartedAt;
            run.Sequence++;
        }

        return [.. earlier.Select(run => run.Id)];
    }

    private async Task<GroceryRun> CreateAsync(GroceryRunState state, CancellationToken cancellationToken)
    {
        // An account deleted since the run started can't be referenced any more; the run goes on without it.
        string[] people = [state.ShopperId, .. state.ParticipantIds];
        List<string> existing = await dbContext.Users
            .Where(user => people.Contains(user.Id))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        GroceryRun run = new()
        {
            Id = state.RunId,
            Sequence = 0,
            Status = state.Status,
            ShopperId = existing.Contains(state.ShopperId) ? state.ShopperId : null,
            StartedAt = state.StartedAt,
            LastCallEndsAt = state.LastCallEndsAt,
            ExpiresAt = state.ExpiresAt,
            Participants = [.. state.ParticipantIds
                .Where(existing.Contains)
                .Select(userId => new GroceryRunParticipant { RunId = state.RunId, UserId = userId })],
        };
        dbContext.GroceryRuns.Add(run);
        return run;
    }
}
