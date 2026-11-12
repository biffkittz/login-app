using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Infrastructure.Temporal;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// With Temporal configured but unreachable (an outage, a revoked key), the list must go on working: only grocery runs
/// are unavailable, and the app stays ready for traffic. Needs no Temporal server.
/// </summary>
public sealed class TemporalOutageTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhenTemporalIsUnreachable_TheListWorks_RunsAreUnavailable_AndTheAppStaysReady()
    {
        // Arrange: nothing listens on port 1.
        await using TemporalApiFactory factory = await TemporalApiFactory.CreateAsync("127.0.0.1:1");
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");

        // Act
        GroceryItemResponse milk = await ada.AddItemAsync("Milk");
        GroceryListResponse list = await ada.GetListAsync();
        using HttpResponseMessage start = await ada.StartRunAsync(lastCallMinutes: 15);
        HealthReport health = await factory.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(CancellationToken);
        using HttpClient probe = factory.CreateClient();
        using HttpResponseMessage readiness = await probe.GetAsync(new Uri("/health/ready", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Contains(list.Items, item => item.Id == milk.Id);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
        ProblemDetails? problem = await start.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Contains("aren't available right now", problem?.Title, StringComparison.Ordinal);
        Assert.Equal(HealthStatus.Degraded, health.Entries[TemporalWorkerHealthCheck.Name].Status);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
    }

    [Fact]
    public async Task WithoutTemporal_RunsAreReportedAsNotSetUp()
    {
        // Arrange
        await using LoginApiFactory factory = new();
        await factory.InitializeAsync();
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");

        // Act
        GroceryListResponse list = await ada.GetListAsync();
        using HttpResponseMessage start = await ada.StartRunAsync(lastCallMinutes: 15);

        // Assert
        Assert.False(list.Settings.GroceryRunsAvailable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
        ProblemDetails? problem = await start.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Contains("aren't set up", problem?.Title, StringComparison.Ordinal);
    }
}
