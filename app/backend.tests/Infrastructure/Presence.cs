using LoginApp.Api.Features.GroceryList.Runs;
using Microsoft.Extensions.DependencyInjection;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Waits for the hub's count of who has the list open (<see cref="GroceryListPresence"/>), which a grocery run reads.
/// </summary>
internal static class Presence
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits until every one of <paramref name="userIds"/> has the list open.</summary>
    /// <param name="services">The app's services.</param>
    /// <param name="userIds">The users.</param>
    /// <returns>A task that completes once they are all online; fails the test after a timeout.</returns>
    public static async Task WaitUntilOnlineAsync(IServiceProvider services, params string[] userIds)
    {
        GroceryListPresence presence = services.GetRequiredService<GroceryListPresence>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_timeout);
        while (!userIds.All(presence.OnlineUserIds().Contains))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
