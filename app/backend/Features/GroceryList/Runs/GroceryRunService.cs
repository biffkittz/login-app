using LoginApp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The API's side of grocery runs: checks a request against the run as the database has it, then starts or signals
/// the run's workflow, which makes the change and announces it.
/// </summary>
/// <remarks>
/// Signals don't answer, so the checks here are what tell a user their tap came too late, or wasn't theirs to make. The
/// workflow checks again (it ignores a signal that doesn't apply), so a check that raced the workflow changes nothing.
/// </remarks>
/// <param name="dbContext">The application database.</param>
/// <param name="store">The runs, as their workflows last reported them.</param>
/// <param name="coordinator">Starts and signals workflows.</param>
/// <param name="presence">Who has the list open, to ask during last call.</param>
/// <param name="options">The lengths of last call on offer and the shopping time limit.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed class GroceryRunService(
    ApplicationDbContext dbContext,
    GroceryRunStore store,
    IGroceryRunCoordinator coordinator,
    GroceryListPresence presence,
    IOptions<GroceryRunOptions> options,
    TimeProvider timeProvider,
    ILogger<GroceryRunService> logger)
{
    // How often, and how far apart, a start is tried while the previous run's workflow completes: a second at most.
    private const int StartAttempts = 5;

    private static readonly TimeSpan s_startRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Starts a grocery run with <paramref name="userId"/> as the shopper, asking everyone else who has the list open.
    /// </summary>
    /// <param name="userId">The signed-in user, who is going shopping.</param>
    /// <param name="lastCallMinutes">How long last call lasts at most: one of the lengths offered.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new run's ID, or why it didn't start.</returns>
    public async Task<(GroceryRunCommandStatus Status, Guid? RunId)> StartAsync(
        string userId,
        int lastCallMinutes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!coordinator.IsAvailable)
        {
            return (GroceryRunCommandStatus.NotAvailable, null);
        }

        if (!options.Value.LastCallMinutes.Contains(lastCallMinutes))
        {
            return (GroceryRunCommandStatus.InvalidLastCall, null);
        }

        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return (GroceryRunCommandStatus.UnknownUser, null);
        }

        // Temporal refuses a second run anyway (one workflow per ID); checking first spares the round trip.
        if (await store.FindCurrentRunAsync(cancellationToken) is not null)
        {
            GroceryRunLog.AlreadyUnderWay(logger, userId);
            return (GroceryRunCommandStatus.AlreadyUnderWay, null);
        }

        // "Whoever is online": the people with the list open now, other than the shopper.
        string[] participantIds = [.. presence.OnlineUserIds().Where(id => !string.Equals(id, userId, StringComparison.Ordinal))];
        GroceryRunWorkflowInput input = new(
            Guid.CreateVersion7(timeProvider.GetUtcNow()),
            userId,
            participantIds,
            TimeSpan.FromMinutes(lastCallMinutes),
            TimeSpan.FromMinutes(options.Value.ShoppingTimeLimitMinutes));

        GroceryRunCoordinatorResult result = await StartWorkflowAsync(input, cancellationToken);
        switch (result)
        {
            case GroceryRunCoordinatorResult.Succeeded:
                GroceryRunLog.RunStarted(logger, userId, input.RunId, lastCallMinutes, participantIds.Length);
                return (GroceryRunCommandStatus.Accepted, input.RunId);
            case GroceryRunCoordinatorResult.AlreadyRunning:
                GroceryRunLog.AlreadyUnderWay(logger, userId);
                return (GroceryRunCommandStatus.AlreadyUnderWay, null);
            default:
                return (GroceryRunCommandStatus.Unreachable, null);
        }
    }

    /// <summary>
    /// Sends <paramref name="signal"/> to run <paramref name="runId"/> on behalf of <paramref name="userId"/>, if the
    /// run is under way and the signal is theirs to send now.
    /// </summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="runId">The run the user sees.</param>
    /// <param name="signal">The signal.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether the signal went to the workflow, or why not.</returns>
    public async Task<GroceryRunCommandStatus> SignalAsync(
        string userId,
        Guid runId,
        GroceryRunSignalKind signal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!coordinator.IsAvailable)
        {
            return GroceryRunCommandStatus.NotAvailable;
        }

        if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return GroceryRunCommandStatus.UnknownUser;
        }

        GroceryRun? run = await store.FindCurrentRunAsync(cancellationToken);
        if (run is null || run.Id != runId)
        {
            return GroceryRunCommandStatus.RunOver;
        }

        GroceryRunCommandStatus check = Check(run, userId, signal, timeProvider.GetUtcNow());
        if (check != GroceryRunCommandStatus.Accepted)
        {
            return check;
        }

        // Saying "I'm good" twice is harmless; the second time needn't trouble Temporal.
        if (signal == GroceryRunSignalKind.ImGood && run.Participants.Exists(participant => participant.UserId == userId && participant.IsReady))
        {
            return GroceryRunCommandStatus.Accepted;
        }

        GroceryRunCoordinatorResult result = await coordinator.SignalAsync(signal, new GroceryRunSignal(runId, userId), cancellationToken);
        switch (result)
        {
            case GroceryRunCoordinatorResult.Succeeded:
                GroceryRunLog.SignalSent(logger, userId, signal, runId);
                return GroceryRunCommandStatus.Accepted;
            case GroceryRunCoordinatorResult.NotRunning:
                return GroceryRunCommandStatus.RunOver;
            default:
                return GroceryRunCommandStatus.Unreachable;
        }
    }

    // A run's last activity saves its end, and only then does its workflow complete, a few milliseconds later. A run
    // started in between finds the workflow ID still taken, although the database (and every phone) shows no run. So
    // while the database still shows none, try again for a moment; a run someone else really started shows up there.
    private async Task<GroceryRunCoordinatorResult> StartWorkflowAsync(GroceryRunWorkflowInput input, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            GroceryRunCoordinatorResult result = await coordinator.StartAsync(input, cancellationToken);
            if (result != GroceryRunCoordinatorResult.AlreadyRunning || attempt == StartAttempts)
            {
                return result;
            }

            await Task.Delay(s_startRetryDelay, timeProvider, cancellationToken);
            if (await store.FindCurrentRunAsync(cancellationToken) is not null)
            {
                return result;
            }
        }
    }

    // The rules a signal must pass, judged on the run as the database has it.
    private static GroceryRunCommandStatus Check(GroceryRun run, string userId, GroceryRunSignalKind signal, DateTimeOffset now)
    {
        bool isShopper = string.Equals(run.ShopperId, userId, StringComparison.Ordinal);
        bool inLastCall = run.Status == GroceryRunStatus.LastCall && now < run.LastCallEndsAt;
        return signal switch
        {
            GroceryRunSignalKind.ImGood when !run.Participants.Exists(participant => participant.UserId == userId) =>
                GroceryRunCommandStatus.NotAsked,
            GroceryRunSignalKind.ImGood or GroceryRunSignalKind.StartShoppingNow or GroceryRunSignalKind.Cancel when !inLastCall =>
                GroceryRunCommandStatus.LastCallOver,
            GroceryRunSignalKind.StartShoppingNow or GroceryRunSignalKind.Cancel or GroceryRunSignalKind.FinishShopping when !isShopper =>
                GroceryRunCommandStatus.NotShopper,
            GroceryRunSignalKind.FinishShopping when run.Status != GroceryRunStatus.Shopping =>
                GroceryRunCommandStatus.NotShoppingYet,
            _ => GroceryRunCommandStatus.Accepted,
        };
    }
}
