using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Who has the list open (<see cref="GroceryListPresence"/>): the people a grocery run asks during last call.
/// </summary>
/// <param name="factory">The in-memory API host.</param>
public sealed class GroceryListPresenceTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AUserWithTwoPhones_StaysOnlineUntilBothAreClosed()
    {
        // Arrange
        GroceryListPresence presence = new();

        // Act
        presence.Connected("ada");
        presence.Connected("ada");
        presence.Connected("ben");
        presence.Disconnected("ada");
        IReadOnlyList<string> afterOne = presence.OnlineUserIds();
        presence.Disconnected("ada");
        presence.Disconnected("nobody");

        // Assert
        Assert.Equal(["ada", "ben"], afterOne);
        Assert.Equal(["ben"], presence.OnlineUserIds());
    }

    [Fact]
    public async Task TheHub_CountsAUserOnlineWhileTheirConnectionIsOpen()
    {
        // Arrange
        GroceryListPresence presence = factory.Services.GetRequiredService<GroceryListPresence>();
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        RealtimeListener phone = new(factory, ada.AccessToken);

        // Act
        await phone.StartAsync();
        await Presence.WaitUntilOnlineAsync(factory.Services, ada.UserId);
        await phone.DisposeAsync();

        // Assert: counted out again once the connection closes.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_timeout);
        while (presence.OnlineUserIds().Contains(ada.UserId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
