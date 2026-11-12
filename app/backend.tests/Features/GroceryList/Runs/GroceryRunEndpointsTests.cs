using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// The <c>/api/grocery-list/run</c> endpoints' rules, with a stand-in for Temporal: who may start a run and send which
/// signal when, and what the API tells them otherwise. The workflow itself is covered by
/// <see cref="GroceryRunWorkflowTests"/>, and the two together by <see cref="GroceryRunEndToEndTests"/>.
/// </summary>
/// <remarks>Tests share one database, and only one run can be under way: each sets up the run it needs.</remarks>
/// <param name="factory">The in-memory API host.</param>
public sealed class GroceryRunEndpointsTests(GroceryRunApiFactory factory) : IClassFixture<GroceryRunApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private FakeGroceryRunCoordinator Coordinator => factory.Coordinator;

    [Fact]
    public async Task GetList_WithoutARun_SaysRunsAreAvailableAndOffersTheLastCallLengths()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await factory.EndAnyRunAsync(ada.UserId);

        // Act
        GroceryListResponse list = await ada.GetListAsync();

        // Assert
        Assert.True(list.Settings.GroceryRunsAvailable);
        Assert.Equal([15, 30, 60, 120], list.LastCallMinutes);
        Assert.Null(list.Run);
    }

    [Fact]
    public async Task StartRun_AsksEveryoneElseWhoHasTheListOpen()
    {
        // Arrange: Ben has the list open, Cy doesn't, and the shopper's own phone doesn't count.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        using Shopper cy = await Shopper.SignUpAsync(factory, "Cy");
        await factory.EndAnyRunAsync(ada.UserId);
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await using RealtimeListener bensPhone = new(factory, ben.AccessToken);
        await adasPhone.StartAsync();
        await bensPhone.StartAsync();
        await factory.WaitUntilOnlineAsync(ada.UserId, ben.UserId);

        // Act
        using HttpResponseMessage response = await ada.StartRunAsync(lastCallMinutes: 30);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        GroceryRunStartedResponse? started = await response.Content.ReadFromJsonAsync<GroceryRunStartedResponse>(CancellationToken);
        GroceryRunWorkflowInput input = Assert.Single(Coordinator.Started, candidate => candidate.ShopperId == ada.UserId);
        Assert.Equal(started?.RunId, input.RunId);
        Assert.Contains(ben.UserId, input.ParticipantIds);
        Assert.DoesNotContain(cy.UserId, input.ParticipantIds);
        Assert.DoesNotContain(ada.UserId, input.ParticipantIds);
        Assert.Equal(TimeSpan.FromMinutes(30), input.LastCallWindow);
        Assert.Equal(TimeSpan.FromMinutes(240), input.ShoppingTimeLimit);
    }

    [Fact]
    public async Task StartRun_WithALengthThatIsNotOffered_ReturnsAValidationProblem()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await factory.EndAnyRunAsync(ada.UserId);

        // Act
        using HttpResponseMessage response = await ada.StartRunAsync(lastCallMinutes: 45);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.Contains(nameof(StartGroceryRunRequest.LastCallMinutes), problem!.Errors.Keys);
        Assert.DoesNotContain(Coordinator.Started, input => input.ShopperId == ada.UserId);
    }

    [Fact]
    public async Task StartRun_WhileARunIsUnderWay_ReturnsConflictWithoutAskingTemporal()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await factory.ApplyAsync(RunStates.LastCall(ada.UserId, [ben.UserId]));

        // Act
        using HttpResponseMessage response = await ben.StartRunAsync(lastCallMinutes: 15);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "already under way");
        Assert.DoesNotContain(Coordinator.Started, input => input.ShopperId == ben.UserId);
    }

    [Theory]
    [InlineData(nameof(GroceryRunCoordinatorResult.AlreadyRunning), HttpStatusCode.Conflict)]
    [InlineData(nameof(GroceryRunCoordinatorResult.Unreachable), HttpStatusCode.ServiceUnavailable)]
    public async Task StartRun_WhenTemporalRefusesOrIsUnreachable_SaysSo(string result, HttpStatusCode expected)
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await factory.EndAnyRunAsync(ada.UserId);
        Coordinator.NextResult = Enum.Parse<GroceryRunCoordinatorResult>(result);

        try
        {
            // Act
            using HttpResponseMessage response = await ada.StartRunAsync(lastCallMinutes: 15);

            // Assert
            Assert.Equal(expected, response.StatusCode);
        }
        finally
        {
            Coordinator.NextResult = GroceryRunCoordinatorResult.Succeeded;
        }
    }

    [Fact]
    public async Task ImGood_FromSomeoneAsked_SignalsTheWorkflow()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage response = await ben.PostRunActionAsync(run.RunId, "ready");

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(Coordinator.Signals, sent => sent.Signal == GroceryRunSignalKind.ImGood && sent.Payload == new GroceryRunSignal(run.RunId, ben.UserId));
    }

    [Fact]
    public async Task ImGood_FromSomeoneAlreadyReady_IsAcceptedWithoutSignallingAgain()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId], readyUserIds: [ben.UserId]);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage response = await ben.PostRunActionAsync(run.RunId, "ready");

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(Coordinator.Signals, sent => sent.Payload.RunId == run.RunId);
    }

    [Fact]
    public async Task ImGood_FromSomeoneWhoWasNotAsked_ReturnsConflict()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        using Shopper cy = await Shopper.SignUpAsync(factory, "Cy");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage fromCy = await cy.PostRunActionAsync(run.RunId, "ready");
        using HttpResponseMessage fromShopper = await ada.PostRunActionAsync(run.RunId, "ready");

        // Assert
        await AssertProblemAsync(fromCy, HttpStatusCode.Conflict, "Nobody is waiting on you");
        await AssertProblemAsync(fromShopper, HttpStatusCode.Conflict, "Nobody is waiting on you");
        Assert.DoesNotContain(Coordinator.Signals, sent => sent.Payload.RunId == run.RunId);
    }

    [Fact]
    public async Task ImGood_ForARunThatIsNotUnderWay_ReturnsConflict()
    {
        // Arrange: the phone shows a run that has since been replaced.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState old = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(old);
        await factory.ApplyAsync(RunStates.LastCall(ada.UserId, [ben.UserId]));

        // Act
        using HttpResponseMessage response = await ben.PostRunActionAsync(old.RunId, "ready");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "is over");
    }

    [Fact]
    public async Task ImGood_AfterLastCall_ReturnsConflict()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(run);
        await factory.ApplyAsync(RunStates.Shopping(run));

        // Act
        using HttpResponseMessage response = await ben.PostRunActionAsync(run.RunId, "ready");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "Last call is over");
    }

    [Theory]
    [InlineData("start-shopping", nameof(GroceryRunSignalKind.StartShoppingNow))]
    [InlineData("cancel", nameof(GroceryRunSignalKind.Cancel))]
    public async Task ShopperActionsDuringLastCall_FromTheShopper_SignalTheWorkflow(string action, string expectedSignal)
    {
        // Arrange
        GroceryRunSignalKind signal = Enum.Parse<GroceryRunSignalKind>(expectedSignal);
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryRunState run = RunStates.LastCall(ada.UserId, participantIds: []);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage response = await ada.PostRunActionAsync(run.RunId, action);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(Coordinator.Signals, sent => sent.Signal == signal && sent.Payload == new GroceryRunSignal(run.RunId, ada.UserId));
    }

    [Theory]
    [InlineData("start-shopping")]
    [InlineData("cancel")]
    public async Task ShopperActionsDuringLastCall_FromSomeoneElse_AreForbidden(string action)
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage response = await ben.PostRunActionAsync(run.RunId, action);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Only the shopper");
    }

    [Fact]
    public async Task Finish_FromTheShopperWhileShopping_SignalsTheWorkflow()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        await factory.ApplyAsync(run);
        await factory.ApplyAsync(RunStates.Shopping(run));

        // Act
        using HttpResponseMessage fromBen = await ben.PostRunActionAsync(run.RunId, "finish");
        using HttpResponseMessage fromAda = await ada.PostRunActionAsync(run.RunId, "finish");

        // Assert
        await AssertProblemAsync(fromBen, HttpStatusCode.Forbidden, "Only the shopper");
        Assert.Equal(HttpStatusCode.Accepted, fromAda.StatusCode);
        Assert.Contains(Coordinator.Signals, sent => sent.Signal == GroceryRunSignalKind.FinishShopping && sent.Payload == new GroceryRunSignal(run.RunId, ada.UserId));
    }

    [Fact]
    public async Task Finish_DuringLastCall_ReturnsConflict()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryRunState run = RunStates.LastCall(ada.UserId, participantIds: []);
        await factory.ApplyAsync(run);

        // Act
        using HttpResponseMessage response = await ada.PostRunActionAsync(run.RunId, "finish");

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "hasn't started");
    }

    [Fact]
    public async Task Signal_WhenTheWorkflowHasEnded_ReturnsConflict()
    {
        // Arrange: the database still shows the run, but its workflow is gone (it was terminated, say).
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryRunState run = RunStates.LastCall(ada.UserId, participantIds: []);
        await factory.ApplyAsync(run);
        Coordinator.NextResult = GroceryRunCoordinatorResult.NotRunning;

        try
        {
            // Act
            using HttpResponseMessage response = await ada.PostRunActionAsync(run.RunId, "cancel");

            // Assert
            await AssertProblemAsync(response, HttpStatusCode.Conflict, "is over");
        }
        finally
        {
            Coordinator.NextResult = GroceryRunCoordinatorResult.Succeeded;
        }
    }

    [Fact]
    public async Task LastCall_LocksTheListAgainstRemovingAndMovingButNotAgainstAddingAndChanging()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await factory.EndAnyRunAsync(ada.UserId);
        GroceryItemResponse milk = await ada.AddItemAsync($"Milk {Guid.NewGuid():N}");
        GroceryItemResponse eggs = await ada.AddItemAsync($"Eggs {Guid.NewGuid():N}");
        await factory.ApplyAsync(RunStates.LastCall(ada.UserId, [ben.UserId]));

        // Act
        using HttpResponseMessage delete = await ben.DeleteItemAsync(milk.Id);
        using HttpResponseMessage move = await ben.MoveItemAsync(milk.Id, eggs.Id);
        using HttpResponseMessage add = await ben.PostItemAsync(new { id = Guid.NewGuid(), name = $"Butter {Guid.NewGuid():N}" });
        using HttpResponseMessage change = await ben.PatchItemAsync(eggs.Id, new { quantity = "a dozen" });

        // Assert
        await AssertProblemAsync(delete, HttpStatusCode.Conflict, "last call");
        await AssertProblemAsync(move, HttpStatusCode.Conflict, "last call");
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
    }

    [Theory]
    [InlineData("shopping")] // the list is final and open for any change
    [InlineData("deadline passed")] // last call should be over; the workflow hasn't said so (Temporal is down, say)
    public async Task TheListIsUnlocked_OnceLastCallIsOver(string situation)
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse milk = await ada.AddItemAsync($"Milk {Guid.NewGuid():N}");
        if (situation == "shopping")
        {
            GroceryRunState run = RunStates.LastCall(ada.UserId, participantIds: []);
            await factory.ApplyAsync(run);
            await factory.ApplyAsync(RunStates.Shopping(run));
        }
        else
        {
            await factory.ApplyAsync(RunStates.LastCall(ada.UserId, participantIds: [], endsIn: TimeSpan.FromMinutes(-2)));
        }

        // Act
        using HttpResponseMessage delete = await ada.DeleteItemAsync(milk.Id);

        // Assert
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
    }

    [Fact]
    public async Task GetList_ShowsTheRunUnderWayWithItsParticipants_AndNotOnceItHasExpired()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId], readyUserIds: [ben.UserId]);
        await factory.ApplyAsync(run);

        // Act
        GroceryRunResponse? shown = (await ben.GetListAsync()).Run;
        await factory.ApplyAsync(RunStates.Shopping(run, startedAgo: TimeSpan.FromHours(5)));
        GroceryRunResponse? afterItsTime = (await ben.GetListAsync()).Run;

        // Assert
        Assert.NotNull(shown);
        Assert.Equal(run.RunId, shown.Id);
        Assert.Equal(GroceryRunStatus.LastCall, shown.Status);
        Assert.Equal(ada.UserId, shown.ShopperId);
        Assert.Equal("Ada", shown.ShopperName);
        GroceryRunParticipantResponse participant = Assert.Single(shown.Participants);
        Assert.Equal(new GroceryRunParticipantResponse(ben.UserId, "Ben", Ready: true), participant);
        Assert.Null(afterItsTime);
    }

    [Theory]
    [InlineData("/api/grocery-list/run")]
    [InlineData("/api/grocery-list/run/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1/ready")]
    [InlineData("/api/grocery-list/run/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1/start-shopping")]
    [InlineData("/api/grocery-list/run/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1/cancel")]
    [InlineData("/api/grocery-list/run/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1/finish")]
    public async Task RunEndpoints_WithoutSignIn_ReturnUnauthorized(string path)
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, new { lastCallMinutes = 15 }, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string titleContains)
    {
        Assert.Equal(status, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Contains(titleContains, problem?.Title, StringComparison.OrdinalIgnoreCase);
    }
}
