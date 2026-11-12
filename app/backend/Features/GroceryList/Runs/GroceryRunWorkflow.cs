using System.Linq.Expressions;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// "Speak now or forever hold your peace": one grocery run, from last call to the end of the shopping.
/// </summary>
/// <remarks>
/// <para>
/// <b>Last call.</b> The run opens with a durable timer of <see cref="GroceryRunWorkflowInput.LastCallWindow"/>
/// (<see cref="Workflow.WaitConditionAsync(Func{bool}, TimeSpan, CancellationToken?)"/>), during which everyone can add
/// items but nobody can remove or move them. It wakes early when everyone asked has sent "I'm good"
/// (<see cref="ImGoodAsync"/>), or when the shopper goes early (<see cref="StartShoppingNowAsync"/>) or calls the run
/// off (<see cref="CancelAsync"/>).
/// </para>
/// <para>
/// <b>Shopping.</b> An activity then makes the list final and unlocks it, and the run waits for the shopper's
/// <see cref="FinishShoppingAsync"/>, or ends by itself after <see cref="GroceryRunWorkflowInput.ShoppingTimeLimit"/>.
/// </para>
/// <para>
/// <b>Side effects only in activities.</b> Each change of state is numbered and handed to an activity
/// (<see cref="GroceryRunActivities"/>) that saves it to the database and pushes it to every phone. The workflow itself
/// touches nothing outside: Temporal replays it from its history after a restart or on another worker, and the replay
/// must make exactly the same decisions. Hence <see cref="Workflow.UtcNow"/> instead of the clock, lists instead of
/// hash sets (their order varies between processes), and no logging here.
/// </para>
/// <para>
/// <b>Changing this class.</b> A deployment replays the runs in flight on the new code. A change to the order or kind
/// of timers and activities must take the old path for old runs: wrap it in <see cref="Workflow.Patched(string)"/>.
/// <c>GroceryRunReplayTests</c> replays recorded histories to catch a change that forgot.
/// </para>
/// </remarks>
[Workflow(TypeName)]
internal sealed class GroceryRunWorkflow
{
    /// <summary>The workflow type's name in Temporal. Fixed, so renaming the class can't strand runs in flight.</summary>
    public const string TypeName = "GroceryRun";

    private readonly GroceryRunWorkflowInput _input;
    private readonly List<string> _ready = [];
    private GroceryRunState _state;
    private bool _readyChanged;
    private bool _shopperGoingNow;
    private bool _cancelRequested;
    private bool _finishRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroceryRunWorkflow"/> class. Temporal calls this with the run's
    /// input before any signal handler, so the handlers can rely on the input and state.
    /// </summary>
    /// <param name="input">The run's input.</param>
    [WorkflowInit]
    public GroceryRunWorkflow(GroceryRunWorkflowInput input)
    {
        _input = input;
        DateTimeOffset now = Now;
        DateTimeOffset lastCallEndsAt = now + input.LastCallWindow;
        _state = new GroceryRunState(
            input.RunId,
            Sequence: 0,
            GroceryRunStatus.LastCall,
            input.ShopperId,
            input.ParticipantIds,
            ReadyUserIds: [],
            StartedAt: now,
            lastCallEndsAt,
            ShoppingStartedAt: null,
            EndedAt: null,
            ExpiresAt: lastCallEndsAt + input.ShoppingTimeLimit,
            LastCallOutcome: null);
    }

    /// <summary>Gets the run's current state (the <c>State</c> query, for the Temporal UI and CLI).</summary>
    [WorkflowQuery("State")]
    public GroceryRunState State => _state;

    // Workflow time: the same on every replay.
    private static DateTimeOffset Now => new(Workflow.UtcNow, TimeSpan.Zero);

    // Everyone asked has said "I'm good". With nobody to ask there is nobody to end last call early: it lasts its full
    // time (or until the shopper goes), giving anyone who opens the list meanwhile the chance to add something.
    private bool EveryoneReady => _input.ParticipantIds.Count > 0 && _input.ParticipantIds.All(_ready.Contains);

    /// <summary>
    /// Runs the grocery run to its end.
    /// </summary>
    /// <param name="input">The run's input (also given to the constructor).</param>
    /// <returns>How the run ended.</returns>
    [WorkflowRun]
    public async Task<GroceryRunStatus> RunAsync(GroceryRunWorkflowInput input)
    {
        if (input.LastCallWindow <= TimeSpan.Zero || input.ShoppingTimeLimit <= TimeSpan.Zero)
        {
            // Nothing has been published yet, so failing leaves nothing behind.
            throw new ApplicationFailureException("A grocery run needs a positive last call and shopping time limit.", nonRetryable: true);
        }

        try
        {
            GroceryRunState opened = Next(_state);
            await PublishAsync(activities => activities.OpenLastCallAsync(opened));

            LastCallOutcome? outcome = await WaitOutLastCallAsync();
            if (outcome is not LastCallOutcome lastCallOutcome)
            {
                return await EndAsync(GroceryRunStatus.Cancelled);
            }

            DateTimeOffset now = Now;
            GroceryRunState shopping = Next(_state with
            {
                Status = GroceryRunStatus.Shopping,
                ShoppingStartedAt = now,
                ExpiresAt = now + _input.ShoppingTimeLimit,
                LastCallOutcome = lastCallOutcome,
            });
            await PublishAsync(activities => activities.StartShoppingAsync(shopping));

            bool finished = await Workflow.WaitConditionAsync(() => _finishRequested, _input.ShoppingTimeLimit);
            return await EndAsync(finished ? GroceryRunStatus.Finished : GroceryRunStatus.Expired);
        }
        catch (Exception exception) when (TemporalException.IsCanceledException(exception)
            && _state.Status is GroceryRunStatus.LastCall or GroceryRunStatus.Shopping)
        {
            // Cancelled from outside (the Temporal UI or CLI). The run must still end where the phones can see it, or
            // they would show it, and last call would lock the list, until its deadlines passed. The workflow's own
            // token is cancelled now, so the last activity gets one of its own.
            using CancellationTokenSource detached = new();
            await EndAsync(GroceryRunStatus.Cancelled, detached.Token);
            throw;
        }
    }

    /// <summary>
    /// "I'm good": a participant has nothing more to add. Ignored unless it comes from someone asked during this run's
    /// last call, while it lasts.
    /// </summary>
    /// <param name="signal">Who sent it, for which run.</param>
    /// <returns>A completed task.</returns>
    [WorkflowSignal("ImGood")]
    public Task ImGoodAsync(GroceryRunSignal signal)
    {
        if (signal.RunId == _input.RunId
            && _state.Status == GroceryRunStatus.LastCall
            && _input.ParticipantIds.Contains(signal.UserId)
            && !_ready.Contains(signal.UserId))
        {
            _ready.Add(signal.UserId);
            _readyChanged = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The shopper is leaving now, without waiting for the rest of last call. Ignored from anyone else.
    /// </summary>
    /// <param name="signal">Who sent it, for which run.</param>
    /// <returns>A completed task.</returns>
    [WorkflowSignal("StartShoppingNow")]
    public Task StartShoppingNowAsync(GroceryRunSignal signal)
    {
        if (IsFromShopper(signal) && _state.Status == GroceryRunStatus.LastCall)
        {
            _shopperGoingNow = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The shopper calls the run off during last call. Ignored from anyone else, and once the shopping has begun (the
    /// shopper finishes instead).
    /// </summary>
    /// <param name="signal">Who sent it, for which run.</param>
    /// <returns>A completed task.</returns>
    [WorkflowSignal("Cancel")]
    public Task CancelAsync(GroceryRunSignal signal)
    {
        if (IsFromShopper(signal) && _state.Status == GroceryRunStatus.LastCall)
        {
            _cancelRequested = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The shopper is done. Ignored from anyone else, and before the shopping has begun.
    /// </summary>
    /// <param name="signal">Who sent it, for which run.</param>
    /// <returns>A completed task.</returns>
    [WorkflowSignal("FinishShopping")]
    public Task FinishShoppingAsync(GroceryRunSignal signal)
    {
        if (IsFromShopper(signal) && _state.Status == GroceryRunStatus.Shopping)
        {
            _finishRequested = true;
        }

        return Task.CompletedTask;
    }

    // Each activity takes the state to publish as its argument: a new copy every time, numbered one higher.
    private static Task PublishAsync(
        Expression<Func<GroceryRunActivities, Task>> activity,
        CancellationToken? cancellationToken = null) =>
        Workflow.ExecuteActivityAsync(activity, new ActivityOptions
        {
            // Saving one row and queueing a message takes milliseconds; a stuck attempt is abandoned and retried.
            StartToCloseTimeout = TimeSpan.FromSeconds(30),

            // Retried until it works: a run can't go on without its state reaching the database (that is what locks
            // and unlocks the list). A database outage just delays the run.
            RetryPolicy = new RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(1),
                BackoffCoefficient = 2,
                MaximumInterval = TimeSpan.FromMinutes(1),
            },
            CancellationToken = cancellationToken,
        });

    private bool IsFromShopper(GroceryRunSignal signal) =>
        signal.RunId == _input.RunId && string.Equals(signal.UserId, _input.ShopperId, StringComparison.Ordinal);

    // Waits for last call to end, publishing each "I'm good" as it comes. Returns why it ended, or null when the
    // shopper called the run off.
    private async Task<LastCallOutcome?> WaitOutLastCallAsync()
    {
        while (true)
        {
            if (_cancelRequested)
            {
                return null;
            }

            if (_shopperGoingNow)
            {
                return LastCallOutcome.ShopperWentEarly;
            }

            if (EveryoneReady)
            {
                return LastCallOutcome.EveryoneReady;
            }

            // Several "I'm good"s that arrived together are published together.
            if (_readyChanged)
            {
                _readyChanged = false;
                GroceryRunState recorded = Next(_state);
                await PublishAsync(activities => activities.RecordReadyAsync(recorded));
                continue;
            }

            TimeSpan remaining = _state.LastCallEndsAt - Now;
            if (remaining <= TimeSpan.Zero
                || !await Workflow.WaitConditionAsync(() => _readyChanged || _shopperGoingNow || _cancelRequested, remaining))
            {
                return LastCallOutcome.TimeUp;
            }
        }
    }

    private async Task<GroceryRunStatus> EndAsync(GroceryRunStatus status, CancellationToken? cancellationToken = null)
    {
        GroceryRunState ended = Next(_state with { Status = status, EndedAt = Now });
        await PublishAsync(activities => activities.EndRunAsync(ended), cancellationToken);
        return status;
    }

    // The next state to publish: one higher than the last, with the participants who are ready so far.
    private GroceryRunState Next(GroceryRunState state)
    {
        _state = state with { Sequence = _state.Sequence + 1, ReadyUserIds = [.. _ready] };
        return _state;
    }
}
