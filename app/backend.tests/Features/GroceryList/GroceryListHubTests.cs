using System.Net;
using System.Security.Cryptography;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// The realtime side of the grocery list: who may connect to <see cref="GroceryListHub"/>, and that every saved
/// change reaches every connected client.
/// </summary>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class GroceryListHubTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    [Fact]
    public async Task EverySavedChange_ReachesOtherUsersInRealtime()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper grace = await Shopper.SignUpAsync(factory, "Grace");
        await using RealtimeListener gracesPhone = new(factory, grace.AccessToken);
        await gracesPhone.StartAsync();
        GroceryItemResponse bread = await ada.AddItemAsync("Bread");
        Assert.Equal(bread, Assert.Single((await gracesPhone.NextChangeAsync()).SavedItems));

        // Act + Assert: added
        GroceryItemResponse milk = await ada.AddItemAsync("Milk");
        GroceryListChange added = await gracesPhone.NextChangeAsync();
        Assert.Equal(milk, Assert.Single(added.SavedItems));
        Assert.Equal("Ada", milk.LastModifiedByName);

        // Act + Assert: changed
        using HttpResponseMessage updated = await ada.PatchItemAsync(milk.Id, new { quantity = "2" });
        GroceryItemResponse changed = Assert.Single((await gracesPhone.NextChangeAsync()).SavedItems);
        Assert.Equal("2", changed.Quantity);
        Assert.Equal(milk.Version + 1, changed.Version);

        // Act + Assert: moved
        using HttpResponseMessage moved = await ada.MoveItemAsync(milk.Id, afterItemId: bread.Id);
        GroceryItemResponse movedMilk = Assert.Single((await gracesPhone.NextChangeAsync()).SavedItems);
        Assert.True(movedMilk.Position > bread.Position);

        // Act + Assert: deleted
        using HttpResponseMessage deleted = await ada.DeleteItemAsync(milk.Id);
        GroceryListChange removed = await gracesPhone.NextChangeAsync();
        Assert.Equal([milk.Id], removed.DeletedItemIds);
        Assert.Empty(removed.SavedItems);
    }

    [Fact]
    public async Task Change_IsAlsoSentToTheAuthorsOwnConnection()
    {
        // Arrange: the author's other phone, and its own tab, must stay in step too.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await adasPhone.StartAsync();

        // Act
        GroceryItemResponse item = await ada.AddItemAsync("Lemons");

        // Assert
        Assert.Equal(item, Assert.Single((await adasPhone.NextChangeAsync()).SavedItems));
    }

    [Fact]
    public async Task Connection_WithoutSignIn_IsRefused()
    {
        // Arrange
        await using RealtimeListener anonymous = new(factory, accessToken: null);

        // Act
        Exception error = await Assert.ThrowsAnyAsync<Exception>(anonymous.StartAsync);

        // Assert: the WebSocket upgrade itself was answered with 401.
        Assert.Contains(((int)HttpStatusCode.Unauthorized).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_OpenedByAPageOnAnotherSite_IsClosed()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        await using RealtimeListener hijacked = new(factory, shopper.AccessToken, origin: "https://evil.example");

        // Act
        await hijacked.StartAsync();

        // Assert
        await hijacked.ClosedAsync();
    }

    [Fact]
    public async Task Connection_IsClosedWhenTheSessionExpires()
    {
        // Arrange: a genuine token for a real account, but one that expires in two seconds.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        string shortLivedToken = CreateToken(shopper.UserId, lifetime: TimeSpan.FromSeconds(2));
        await using RealtimeListener listener = new(factory, shortLivedToken);
        await listener.StartAsync();

        // Act + Assert
        await listener.ClosedAsync();
    }

    [Theory]
    [InlineData(null, "app.biffkittz.com", false)] // not a browser: judged by its token alone
    [InlineData("https://app.biffkittz.com", "app.biffkittz.com", false)]
    [InlineData("https://APP.biffkittz.com", "app.biffkittz.com", false)]
    [InlineData("http://localhost:5173", "localhost:5173", false)] // the Vite dev server's proxy
    [InlineData("https://evil.example", "app.biffkittz.com", true)]
    [InlineData("https://app.biffkittz.com.evil.example", "app.biffkittz.com", true)]
    [InlineData("http://localhost:5174", "localhost:5173", true)]
    [InlineData("null", "app.biffkittz.com", true)] // sandboxed frames and file:// pages
    public void IsCrossOrigin_ComparesTheOriginWithTheHost(string? origin, string host, bool expected)
    {
        // Arrange
        DefaultHttpContext context = new();
        context.Request.Host = new HostString(host);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        // Act
        bool isCrossOrigin = GroceryListHub.IsCrossOrigin(context.Request);

        // Assert
        Assert.Equal(expected, isCrossOrigin);
    }

    // Signed with the test host's real key, so it is genuine; only its lifetime is unusual.
    private static string CreateToken(string userId, TimeSpan lifetime)
    {
        DateTime now = DateTime.UtcNow;
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = "login-app",
            Audience = "login-app",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(LoginApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId,
                [JwtRegisteredClaimNames.Jti] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
