using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// The database's copy of the runs (<see cref="GroceryRunStore.ApplyAsync"/>): repeated and late states change nothing,
/// and a new run ends one its workflow never ended.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class GroceryRunStoreTests(GroceryRunApiFactory factory) : IClassFixture<GroceryRunApiFactory>
{
    [Fact]
    public async Task ApplyingANewRun_SavesItWithItsShopperAndParticipants()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);

        // Act
        GroceryRunApplyResult result = await factory.ApplyAsync(run);

        // Assert
        Assert.True(result.Applied);
        Assert.Null(result.PreviousStatus);
        Assert.Equal(run.RunId, result.Run.Id);
        Assert.Equal(1, result.Run.Sequence);
        Assert.Equal("Ada", result.Run.ShopperName);
        Assert.Equal(new GroceryRunParticipantResponse(ben.UserId, "Ben", Ready: false), Assert.Single(result.Run.Participants));
    }

    [Fact]
    public async Task ApplyingAStateAgain_ChangesNothing()
    {
        // Arrange: a retried activity.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall(ada.UserId, [ben.UserId]);
        GroceryRunState ready = run with { Sequence = 2, ReadyUserIds = [ben.UserId] };
        await factory.ApplyAsync(run);
        await factory.ApplyAsync(ready);

        // Act
        GroceryRunApplyResult again = await factory.ApplyAsync(ready);

        // Assert: nothing saved, and the run as it stands is there to broadcast again.
        Assert.False(again.Applied);
        Assert.Equal(GroceryRunStatus.LastCall, again.PreviousStatus);
        Assert.Equal(2, again.Run.Sequence);
        Assert.True(Assert.Single(again.Run.Participants).Ready);
    }

    [Fact]
    public async Task ApplyingAnOlderState_DoesNotTakeTheRunBack()
    {
        // Arrange: a late activity.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryRunState run = RunStates.LastCall(ada.UserId, participantIds: []);
        await factory.ApplyAsync(run);
        await factory.ApplyAsync(RunStates.Shopping(run));

        // Act
        GroceryRunApplyResult late = await factory.ApplyAsync(run);

        // Assert
        Assert.False(late.Applied);
        Assert.Equal(GroceryRunStatus.Shopping, late.Run.Status);
    }

    [Fact]
    public async Task ANewRun_EndsAnEarlierRunStillMarkedUnderWay()
    {
        // Arrange: a run whose workflow was terminated before it could end the run.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryRunState stale = RunStates.LastCall(ada.UserId, participantIds: []);
        await factory.ApplyAsync(stale);
        GroceryRunState fresh = RunStates.LastCall(ada.UserId, participantIds: []);

        // Act
        GroceryRunApplyResult result = await factory.ApplyAsync(fresh);

        // Assert
        Assert.Equal([stale.RunId], result.SupersededRunIds);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        GroceryRun earlier = await dbContext.GroceryRuns.SingleAsync(saved => saved.Id == stale.RunId, TestContext.Current.CancellationToken);
        Assert.Equal(GroceryRunStatus.Cancelled, earlier.Status);
        Assert.NotNull(earlier.EndedAt);
        Assert.Equal(fresh.RunId, (await scope.ServiceProvider.GetRequiredService<GroceryRunStore>().GetCurrentAsync(TestContext.Current.CancellationToken))?.Id);
    }

    [Fact]
    public async Task PeopleWhoseAccountsAreGone_AreLeftOut()
    {
        // Arrange: the run started before two accounts were deleted.
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        GroceryRunState run = RunStates.LastCall("deleted-shopper", [ben.UserId, "deleted-participant"]);

        // Act
        GroceryRunApplyResult result = await factory.ApplyAsync(run);

        // Assert
        Assert.Null(result.Run.ShopperId);
        Assert.Null(result.Run.ShopperName);
        Assert.Equal(ben.UserId, Assert.Single(result.Run.Participants).UserId);
    }
}
