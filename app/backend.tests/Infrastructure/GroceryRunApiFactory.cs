using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Features.GroceryList.Runs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="LoginApiFactory"/> where grocery runs are available, with <see cref="Coordinator"/> standing in for
/// Temporal. Runs reach the database the way the workflow's activities put them there
/// (<see cref="GroceryRunStore.ApplyAsync"/>), through <see cref="ApplyAsync"/>.
/// </summary>
/// <remarks>
/// The clock stays the real one (access tokens are issued and checked against it); tests place a run's deadlines
/// before or after now instead of moving time.
/// </remarks>
public sealed class GroceryRunApiFactory : LoginApiFactory
{
    /// <summary>Gets the stand-in for Temporal.</summary>
    internal FakeGroceryRunCoordinator Coordinator { get; } = new();

    /// <summary>Saves a run's state as the workflow's activities would (without broadcasting it).</summary>
    /// <param name="state">The state.</param>
    /// <returns>What the store did.</returns>
    internal async Task<GroceryRunApplyResult> ApplyAsync(GroceryRunState state)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GroceryRunStore>().ApplyAsync(state, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Ends whatever run is under way, so a test starts with none: a new run ends any earlier one still under way, and
    /// this one then ends too.
    /// </summary>
    /// <param name="shopperId">An existing user, to be the throwaway run's shopper.</param>
    /// <returns>A task that completes when no run is under way.</returns>
    internal async Task EndAnyRunAsync(string shopperId)
    {
        GroceryRunState run = RunStates.LastCall(shopperId, participantIds: []);
        await ApplyAsync(run);
        await ApplyAsync(RunStates.Ended(run, GroceryRunStatus.Cancelled));
    }

    /// <summary>
    /// Waits until the hub has counted <paramref name="userIds"/> as having the list open: a connection is counted
    /// just after its handshake, which the client may see first.
    /// </summary>
    /// <param name="userIds">The users.</param>
    /// <returns>A task that completes once they are all online.</returns>
    internal Task WaitUntilOnlineAsync(params string[] userIds) => Presence.WaitUntilOnlineAsync(Services, userIds);

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGroceryRunCoordinator>();
            services.AddSingleton<IGroceryRunCoordinator>(Coordinator);
        });
    }
}
