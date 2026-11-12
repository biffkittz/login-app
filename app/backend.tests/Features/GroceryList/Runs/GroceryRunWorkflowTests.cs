using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Infrastructure.Temporal;
using Temporalio.Exceptions;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// The grocery run workflow on a real (development) Temporal server: its timers, signals and wait conditions, with
/// stand-in activities that record what it publishes.
/// </summary>
/// <param name="server">The shared development server.</param>
public sealed class GroceryRunWorkflowTests(TemporalDevServer server)
{
    private const string Alice = "alice-0001";
    private const string Bob = "bob-0002";

    // Long enough that a test finishing before it proves the timer didn't decide.
    private static readonly TimeSpan s_longWindow = TimeSpan.FromHours(1);

    // Short enough to wait out in a test.
    private static readonly TimeSpan s_shortWindow = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task LastCall_EndsAsSoonAsEveryoneAskedIsReady()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice, Bob], s_longWindow);

        GroceryRunState opened = await run.Activities.NextAsync("OpenLastCall");
        Assert.Equal(1, opened.Sequence);
        Assert.Equal(GroceryRunStatus.LastCall, opened.Status);
        Assert.Equal([Alice, Bob], opened.ParticipantIds);
        Assert.Empty(opened.ReadyUserIds);
        Assert.Equal(opened.StartedAt + s_longWindow, opened.LastCallEndsAt);

        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(Alice)));
        GroceryRunState recorded = await run.Activities.NextAsync("RecordReady");
        Assert.Equal(2, recorded.Sequence);
        Assert.Equal([Alice], recorded.ReadyUserIds);
        Assert.Equal(GroceryRunStatus.LastCall, recorded.Status);

        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(Bob)));
        GroceryRunState shopping = await run.Activities.NextAsync("StartShopping");
        Assert.Equal(3, shopping.Sequence);
        Assert.Equal(GroceryRunStatus.Shopping, shopping.Status);
        Assert.Equal(LastCallOutcome.EveryoneReady, shopping.LastCallOutcome);
        Assert.Equal([Alice, Bob], shopping.ReadyUserIds);
        Assert.True(shopping.ShoppingStartedAt < opened.LastCallEndsAt);
        Assert.Equal(shopping.ShoppingStartedAt + run.Input.ShoppingTimeLimit, shopping.ExpiresAt);

        await run.SignalAsync(workflow => workflow.FinishShoppingAsync(run.From(WorkflowHarness.ShopperId)));
        GroceryRunState ended = await run.Activities.NextAsync("EndRun");
        Assert.Equal(4, ended.Sequence);
        Assert.Equal(GroceryRunStatus.Finished, ended.Status);
        Assert.NotNull(ended.EndedAt);
        Assert.Equal(GroceryRunStatus.Finished, await run.Handle.GetResultAsync());
        await run.SaveHistoryAsync("everyone-ready-then-finished");
    }

    [Fact]
    public async Task LastCallAndShopping_EndWhenTheirTimeIsUp()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice, Bob], s_shortWindow, shoppingTimeLimit: s_shortWindow);
        GroceryRunState opened = await run.Activities.NextAsync("OpenLastCall");

        // Only one of the two is ready, so the timer decides.
        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(Bob)));
        Assert.Equal([Bob], (await run.Activities.NextAsync("RecordReady")).ReadyUserIds);

        GroceryRunState shopping = await run.Activities.NextAsync("StartShopping");
        Assert.Equal(LastCallOutcome.TimeUp, shopping.LastCallOutcome);
        Assert.True(shopping.ShoppingStartedAt >= opened.LastCallEndsAt);

        GroceryRunState ended = await run.Activities.NextAsync("EndRun");
        Assert.Equal(GroceryRunStatus.Expired, ended.Status);
        Assert.True(ended.EndedAt >= shopping.ShoppingStartedAt + s_shortWindow);
        Assert.Equal(GroceryRunStatus.Expired, await run.Handle.GetResultAsync());
        await run.SaveHistoryAsync("time-up-then-expired");
    }

    [Fact]
    public async Task LastCall_WithNobodyToAsk_LastsItsFullTime()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, participantIds: [], s_shortWindow);
        GroceryRunState opened = await run.Activities.NextAsync("OpenLastCall");

        GroceryRunState shopping = await run.Activities.NextAsync("StartShopping");
        Assert.Equal(LastCallOutcome.TimeUp, shopping.LastCallOutcome);
        Assert.True(shopping.ShoppingStartedAt >= opened.LastCallEndsAt);
    }

    [Fact]
    public async Task StartShoppingNow_FromTheShopper_EndsLastCallAtOnce()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice], s_longWindow);
        await run.Activities.NextAsync("OpenLastCall");

        await run.SignalAsync(workflow => workflow.StartShoppingNowAsync(run.From(WorkflowHarness.ShopperId)));

        GroceryRunState shopping = await run.Activities.NextAsync("StartShopping");
        Assert.Equal(LastCallOutcome.ShopperWentEarly, shopping.LastCallOutcome);
        Assert.Empty(shopping.ReadyUserIds);
        await run.SaveHistoryAsync("shopper-went-early");
    }

    [Fact]
    public async Task Cancel_FromTheShopperDuringLastCall_EndsTheRun()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice], s_longWindow);
        await run.Activities.NextAsync("OpenLastCall");

        await run.SignalAsync(workflow => workflow.CancelAsync(run.From(WorkflowHarness.ShopperId)));

        GroceryRunState ended = await run.Activities.NextAsync("EndRun");
        Assert.Equal(GroceryRunStatus.Cancelled, ended.Status);
        Assert.Null(ended.LastCallOutcome);
        Assert.Null(ended.ShoppingStartedAt);
        Assert.Equal(GroceryRunStatus.Cancelled, await run.Handle.GetResultAsync());
        await run.SaveHistoryAsync("cancelled-by-shopper");
    }

    [Fact]
    public async Task Signals_ThatDontApply_AreIgnored()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice, Bob], s_longWindow);
        await run.Activities.NextAsync("OpenLastCall");

        // Someone who wasn't asked, the shopper, a tap meant for another run, and the shopper's buttons pressed by
        // someone else or at the wrong time.
        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From("stranger-0003")));
        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(WorkflowHarness.ShopperId)));
        await run.SignalAsync(workflow => workflow.ImGoodAsync(new GroceryRunSignal(Guid.NewGuid(), Bob)));
        await run.SignalAsync(workflow => workflow.StartShoppingNowAsync(run.From(Alice)));
        await run.SignalAsync(workflow => workflow.CancelAsync(run.From(Bob)));
        await run.SignalAsync(workflow => workflow.FinishShoppingAsync(run.From(WorkflowHarness.ShopperId)));

        // Signals are handled in order, so the first thing published after them is Alice's readiness, alone.
        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(Alice)));
        await run.SignalAsync(workflow => workflow.ImGoodAsync(run.From(Alice)));
        GroceryRunState recorded = await run.Activities.NextAsync("RecordReady");
        Assert.Equal(2, recorded.Sequence);
        Assert.Equal(GroceryRunStatus.LastCall, recorded.Status);
        Assert.Equal([Alice], recorded.ReadyUserIds);

        // The State query shows the same, and nothing else was published.
        GroceryRunState state = await run.Handle.QueryAsync(workflow => workflow.State);
        Assert.Equal(recorded.Sequence, state.Sequence);
        Assert.Equal(recorded.Status, state.Status);
        Assert.Equal(recorded.ReadyUserIds, state.ReadyUserIds);
        Assert.True(run.Activities.NothingPublished());
    }

    [Fact]
    public async Task CancellingTheWorkflow_StillEndsTheRunForThePhones()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice], s_longWindow);
        await run.Activities.NextAsync("OpenLastCall");

        // As an operator would, from the Temporal UI or CLI.
        await run.Handle.CancelAsync();

        GroceryRunState ended = await run.Activities.NextAsync("EndRun");
        Assert.Equal(GroceryRunStatus.Cancelled, ended.Status);
        WorkflowFailedException failure = await Assert.ThrowsAsync<WorkflowFailedException>(() => run.Handle.GetResultAsync());
        Assert.IsType<CanceledFailureException>(failure.InnerException);
    }

    [Fact]
    public async Task ARunWithoutALastCall_FailsBeforePublishingAnything()
    {
        await using WorkflowHarness run = await WorkflowHarness.StartAsync(server, [Alice], TimeSpan.Zero);

        WorkflowFailedException failure = await Assert.ThrowsAsync<WorkflowFailedException>(() => run.Handle.GetResultAsync());
        Assert.IsType<ApplicationFailureException>(failure.InnerException);
        Assert.True(run.Activities.NothingPublished());
    }
}
