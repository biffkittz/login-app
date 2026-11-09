using System.Globalization;
using System.Net;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// Verifies the per-user rate limit on changes to the grocery list.
/// </summary>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class GroceryListRateLimitingTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    [Fact]
    public async Task Writes_BeyondPermitLimit_AreRejectedForThatUserOnly()
    {
        // Arrange
        const int PermitLimit = 2;
        await using WebApplicationFactory<Program> limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:GroceryList:PermitLimit", PermitLimit.ToString(CultureInfo.InvariantCulture)));
        using Shopper busy = await Shopper.SignUpAsync(limitedFactory);
        using Shopper calm = await Shopper.SignUpAsync(limitedFactory);
        for (int attempt = 0; attempt < PermitLimit; attempt++)
        {
            await busy.AddItemAsync($"Item {attempt}");
        }

        // Act
        using HttpResponseMessage limited = await busy.PostItemAsync(new { name = "One too many" });
        using HttpResponseMessage unaffected = await calm.PostItemAsync(new { name = "Somebody else's item" });

        // Assert: limits are per user, so a family sharing one home network doesn't share one budget.
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.Created, unaffected.StatusCode);
    }

    [Fact]
    public async Task Reads_AreNotRateLimited()
    {
        // Arrange
        await using WebApplicationFactory<Program> limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:GroceryList:PermitLimit", "1"));
        using Shopper shopper = await Shopper.SignUpAsync(limitedFactory);

        // Act + Assert
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage response = await shopper.Http.GetAsync(
                new Uri("/api/grocery-list", UriKind.Relative),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
