using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Cross-cutting behavior of the HTTP pipeline: security headers, routing of unknown API paths and health probes.
/// </summary>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class ApiPipelineTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnknownApiRoute_ReturnsJsonNotFoundInsteadOfTheSpaShell()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Equal(StatusCodes.Status404NotFound, problem?.Status);
    }

    [Fact]
    public async Task Responses_CarryBrowserSecurityHeaders()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Contains("frame-ancestors 'none'", HeaderValue(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", HeaderValue(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("nosniff", HeaderValue(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", HeaderValue(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", HeaderValue(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task ApiResponses_AreNeverCached()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), CancellationToken);

        // Assert
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbes_ReportHealthy(string path)
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static string HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;
}
