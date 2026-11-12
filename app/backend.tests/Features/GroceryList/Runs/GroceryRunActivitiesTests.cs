using System.Diagnostics.Metrics;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Infrastructure.Telemetry;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// The workflow's activities, run in Temporal's in-process activity environment against the real app: each saves the
/// state, pushes the run to every open list, and counts what happened once, however often Temporal runs it.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class GroceryRunActivitiesTests(GroceryRunApiFactory factory) : IClassFixture<GroceryRunApiFactory>
{
    private IMeterFactory MeterFactory => factory.Services.GetRequiredService<IMeterFactory>();

    // The activities aren't registered without Temporal; build them from the app's own parts.
    private GroceryRunActivities Activities => new(
        factory.Services.GetRequiredService<IServiceScopeFactory>(),
        factory.Services.GetRequiredService<IGroceryListNotifier>(),
        factory.Services.GetRequiredService<GroceryRunMetrics>(),
        NullLogger<GroceryRunActivities>.Instance);

    [Fact]
    public async Task EachStep_ReachesEveryPhone_AndIsCountedOnceThoughRepeated()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await using RealtimeListener bensPhone = new(factory, ben.AccessToken);
        await bensPhone.StartAsync();
        using MetricCollector<long> started = new(MeterFactory, ApplicationMeter.Name, "nibbles.grocery_runs.started");
        using MetricCollector<long> lastCalls = new(MeterFactory, ApplicationMeter.Name, "nibbles.grocery_runs.last_calls");
        using MetricCollector<long> ended = new(MeterFactory, ApplicationMeter.Name, "nibbles.grocery_runs.ended");
        GroceryRunState opened = RunStates.LastCall(ada.UserId, [ben.UserId]);
        GroceryRunState shopping = RunStates.Shopping(opened) with { LastCallOutcome = LastCallOutcome.EveryoneReady, ReadyUserIds = [ben.UserId] };
        GroceryRunState finished = RunStates.Ended(shopping);
        ActivityEnvironment environment = new();
        GroceryRunActivities activities = Activities;

        // Act: every step, the first two twice, as retried activities would.
        await environment.RunAsync(() => activities.OpenLastCallAsync(opened));
        await environment.RunAsync(() => activities.OpenLastCallAsync(opened));
        GroceryRunResponse lastCall = await bensPhone.NextRunAsync(run => run.Id == opened.RunId);
        await environment.RunAsync(() => activities.StartShoppingAsync(shopping));
        await environment.RunAsync(() => activities.StartShoppingAsync(shopping));
        GroceryRunResponse shoppingNow = await bensPhone.NextRunAsync(run => run.Id == opened.RunId && run.Status == GroceryRunStatus.Shopping);
        await environment.RunAsync(() => activities.EndRunAsync(finished));
        GroceryRunResponse over = await bensPhone.NextRunAsync(run => run.Id == opened.RunId && run.Status == GroceryRunStatus.Finished);

        // Assert
        Assert.Equal(GroceryRunStatus.LastCall, lastCall.Status);
        Assert.Equal(LastCallOutcome.EveryoneReady, shoppingNow.LastCallOutcome);
        Assert.Equal(3, over.Sequence);
        Assert.Single(started.GetMeasurementSnapshot());
        CollectedMeasurement<long> lastCallEnded = Assert.Single(lastCalls.GetMeasurementSnapshot());
        Assert.Equal("everyone_ready", lastCallEnded.Tags[GroceryRunMetrics.OutcomeTag]);
        CollectedMeasurement<long> runEnded = Assert.Single(ended.GetMeasurementSnapshot(), measurement => measurement.Tags[GroceryRunMetrics.StatusTag] as string == "finished");
        Assert.Equal(1, runEnded.Value);
    }

    [Fact]
    public async Task TheBroadcast_IsCountedAsARunChangedMessage()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await adasPhone.StartAsync();
        await factory.WaitUntilOnlineAsync(ada.UserId);
        using MetricCollector<long> broadcasts = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.broadcasts");
        GroceryRunState opened = RunStates.LastCall(ada.UserId, participantIds: []);

        // Act
        await new ActivityEnvironment().RunAsync(() => Activities.OpenLastCallAsync(opened));
        await adasPhone.NextRunAsync(run => run.Id == opened.RunId);

        // Assert
        Assert.Contains(
            broadcasts.GetMeasurementSnapshot(),
            measurement => measurement.Tags[GroceryListMetrics.EventTag] as string == GroceryListMetrics.RunChangedEvent);
    }
}
