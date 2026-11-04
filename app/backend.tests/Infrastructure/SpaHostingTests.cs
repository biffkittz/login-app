using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Routing of the SPA fallback: client-side routes get index.html, while file-like paths and API paths never do.
/// </summary>
/// <remarks>
/// Uses a temporary web root with a known index.html, so the tests don't depend on a local frontend build.
/// </remarks>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class SpaHostingTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>, IDisposable
{
    private const string ShellMarker = "<!-- spa-shell -->";

    private readonly DirectoryInfo _webRoot = CreateWebRoot();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/some/deep/client/route")]
    public async Task ClientRoute_ServesTheSpaShell(string path)
    {
        // Arrange
        await using WebApplicationFactory<Program> host = CreateHostWithWebRoot();
        using HttpClient client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ShellMarker, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoCache, "The shell must be revalidated so deployments take effect.");
    }

    [Theory]
    [InlineData("/assets/missing-file.js")]
    [InlineData("/api")]
    [InlineData("/api/does-not-exist")]
    public async Task FileAndApiPaths_AreNeverServedTheSpaShell(string path)
    {
        // Arrange
        await using WebApplicationFactory<Program> host = CreateHostWithWebRoot();
        using HttpClient client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(ShellMarker, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRequestWithWrongContentType_KeepsItsUnsupportedMediaTypeStatus()
    {
        // Arrange: the SPA fallback must not swallow API requests that no endpoint accepts.
        await using WebApplicationFactory<Program> host = CreateHostWithWebRoot();
        using HttpClient client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using StringContent body = new("{}", Encoding.UTF8, "text/plain");

        // Act
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/auth/login", UriKind.Relative), body, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <inheritdoc />
    public void Dispose() => _webRoot.Delete(recursive: true);

    private WebApplicationFactory<Program> CreateHostWithWebRoot() =>
        factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot.FullName));

    private static DirectoryInfo CreateWebRoot()
    {
        DirectoryInfo webRoot = Directory.CreateTempSubdirectory("login-app-webroot-");
        File.WriteAllText(
            Path.Combine(webRoot.FullName, "index.html"),
            $"<!doctype html><html><body>{ShellMarker}<div id=\"app\"></div></body></html>");
        return webRoot;
    }
}
