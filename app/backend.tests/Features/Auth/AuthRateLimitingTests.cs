using System.Globalization;
using System.Net;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LoginApp.Api.Tests.Features.Auth;

/// <summary>
/// Verifies the per-client rate limit on the anonymous authentication endpoints.
/// </summary>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class AuthRateLimitingTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    [Fact]
    public async Task Login_BeyondPermitLimit_ReturnsTooManyRequestsWithRetryAfter()
    {
        // Arrange
        const int PermitLimit = 3;
        await using WebApplicationFactory<Program> limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:Authentication:PermitLimit", PermitLimit.ToString(CultureInfo.InvariantCulture)));
        using HttpClient client = limitedFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        for (int attempt = 0; attempt < PermitLimit; attempt++)
        {
            using HttpResponseMessage allowed = await TestAccounts.LoginAsync(client, TestAccounts.UniqueEmail());
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        // Act
        using HttpResponseMessage response = await TestAccounts.LoginAsync(client, TestAccounts.UniqueEmail());

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }
}
