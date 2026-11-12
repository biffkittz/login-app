using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Infrastructure;
using LoginApp.Api.Tests.Infrastructure.Temporal;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Grocery runs from end to end: the API starts and signals the workflow on the development server, the app's own
/// in-process worker runs it, and its activities save the run and push it to the phones.
/// </summary>
/// <param name="server">The shared development server.</param>
public sealed class GroceryRunEndToEndTests(TemporalDevServer server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGroceryRun_GoesFromLastCallThroughShoppingToFinished_OnEveryPhone()
    {
        // Arrange: Ben has the list open; Ada adds milk and goes shopping.
        WorkflowEnvironment environment = await server.GetAsync();
        await using TemporalApiFactory factory = await TemporalApiFactory.CreateAsync(TemporalDevServer.AddressOf(environment));
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await using RealtimeListener bensPhone = new(factory, ben.AccessToken);
        await bensPhone.StartAsync();
        await Presence.WaitUntilOnlineAsync(factory.Services, ben.UserId);
        GroceryItemResponse milk = await ada.AddItemAsync("Milk");

        // Act 1: Ada starts the run.
        using HttpResponseMessage start = await ada.StartRunAsync(lastCallMinutes: 15);
        Guid runId = (await start.Content.ReadFromJsonAsync<GroceryRunStartedResponse>(CancellationToken))!.RunId;
        GroceryRunResponse lastCall = await bensPhone.NextRunAsync(run => run.Id == runId);

        // Assert 1: last call is on everywhere, asks Ben, and locks the list.
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(GroceryRunStatus.LastCall, lastCall.Status);
        Assert.Equal("Ada", lastCall.ShopperName);
        Assert.Equal(new GroceryRunParticipantResponse(ben.UserId, "Ben", Ready: false), Assert.Single(lastCall.Participants));
        Assert.Equal(lastCall.StartedAt + TimeSpan.FromMinutes(15), lastCall.LastCallEndsAt);
        Assert.Equal(runId, (await ben.GetListAsync()).Run?.Id);
        using (HttpResponseMessage delete = await ben.DeleteItemAsync(milk.Id))
        {
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        }

        using (HttpResponseMessage second = await ben.StartRunAsync(lastCallMinutes: 15))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        // Act 2: Ben is good, the only one asked, so last call ends at once.
        using HttpResponseMessage ready = await ben.PostRunActionAsync(runId, "ready");
        GroceryRunResponse shopping = await bensPhone.NextRunAsync(run => run.Id == runId && run.Status == GroceryRunStatus.Shopping);

        // Assert 2: the shopping has begun, long before the 15 minutes were up, and the list is unlocked.
        Assert.Equal(HttpStatusCode.Accepted, ready.StatusCode);
        Assert.Equal(LastCallOutcome.EveryoneReady, shopping.LastCallOutcome);
        Assert.True(Assert.Single(shopping.Participants).Ready);
        Assert.True(shopping.ShoppingStartedAt < lastCall.LastCallEndsAt);
        using (HttpResponseMessage delete = await ben.DeleteItemAsync(milk.Id))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        // Act 3: Ada is done.
        using HttpResponseMessage finish = await ada.PostRunActionAsync(runId, "finish");
        GroceryRunResponse finished = await bensPhone.NextRunAsync(run => run.Id == runId && run.Status == GroceryRunStatus.Finished);

        // Assert 3: the run is over everywhere.
        Assert.Equal(HttpStatusCode.Accepted, finish.StatusCode);
        Assert.NotNull(finished.EndedAt);
        Assert.Null((await ben.GetListAsync()).Run);
    }

    [Fact]
    public async Task TheShopper_CanCallTheRunOff_AndAnotherCanStartAfterwards()
    {
        // Arrange: nobody else has the list open, so last call would last its full hour.
        WorkflowEnvironment environment = await server.GetAsync();
        await using TemporalApiFactory factory = await TemporalApiFactory.CreateAsync(TemporalDevServer.AddressOf(environment));
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await adasPhone.StartAsync();
        using HttpResponseMessage start = await ada.StartRunAsync(lastCallMinutes: 60);
        Guid runId = (await start.Content.ReadFromJsonAsync<GroceryRunStartedResponse>(CancellationToken))!.RunId;
        GroceryRunResponse lastCall = await adasPhone.NextRunAsync(run => run.Id == runId);

        // Act
        using HttpResponseMessage cancel = await ada.PostRunActionAsync(runId, "cancel");
        GroceryRunResponse cancelled = await adasPhone.NextRunAsync(run => run.Id == runId && run.Status == GroceryRunStatus.Cancelled);
        using HttpResponseMessage again = await ada.StartRunAsync(lastCallMinutes: 15);

        // Assert
        Assert.Empty(lastCall.Participants);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Null(cancelled.LastCallOutcome);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task TheCoordinator_RefusesASecondRunAndReportsAnEndedOne()
    {
        // Arrange: the coordinator on its own, as the API uses it.
        WorkflowEnvironment environment = await server.GetAsync();
        await using TemporalApiFactory factory = await TemporalApiFactory.CreateAsync(TemporalDevServer.AddressOf(environment));
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        IGroceryRunCoordinator coordinator = factory.Services.GetRequiredService<IGroceryRunCoordinator>();
        GroceryRunWorkflowInput first = new(Guid.NewGuid(), ada.UserId, [], TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        GroceryRunWorkflowInput second = first with { RunId = Guid.NewGuid() };

        // Act
        GroceryRunCoordinatorResult started = await coordinator.StartAsync(first, CancellationToken);
        GroceryRunCoordinatorResult startedAgain = await coordinator.StartAsync(second, CancellationToken);
        GroceryRunCoordinatorResult cancelled = await coordinator.SignalAsync(GroceryRunSignalKind.Cancel, new GroceryRunSignal(first.RunId, ada.UserId), CancellationToken);
        await environment.Client
            .GetWorkflowHandle(TemporalGroceryRunCoordinator.WorkflowIdFor(factory.TaskQueue))
            .GetResultAsync();
        GroceryRunCoordinatorResult afterTheEnd = await coordinator.SignalAsync(GroceryRunSignalKind.Cancel, new GroceryRunSignal(first.RunId, ada.UserId), CancellationToken);

        // Assert
        Assert.Equal(GroceryRunCoordinatorResult.Succeeded, started);
        Assert.Equal(GroceryRunCoordinatorResult.AlreadyRunning, startedAgain);
        Assert.Equal(GroceryRunCoordinatorResult.Succeeded, cancelled);
        Assert.Equal(GroceryRunCoordinatorResult.NotRunning, afterTheEnd);
    }
}
