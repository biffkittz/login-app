using System.Diagnostics.Metrics;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Infrastructure.Telemetry;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// The realtime hub's metrics through the real API: connected clients, refused connections, and the messages each
/// broadcast sends.
/// </summary>
/// <param name="factory">The in-memory API host, shared by this class only.</param>
public sealed class GroceryListMetricsTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private IMeterFactory MeterFactory => factory.Services.GetRequiredService<IMeterFactory>();

    private GroceryListMetrics Metrics => factory.Services.GetRequiredService<GroceryListMetrics>();

    [Fact]
    public async Task AnAcceptedConnection_IsCountedWhileItIsOpen()
    {
        // Arrange
        await WaitForOpenConnectionsAsync(0);
        using MetricCollector<long> connections = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.connections");
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        RealtimeListener phone = new(factory, shopper.AccessToken);

        // Act
        await phone.StartAsync();
        await connections.WaitForMeasurementsAsync(1, s_timeout);
        long whileOpen = Metrics.OpenConnections;
        await phone.DisposeAsync();
        await connections.WaitForMeasurementsAsync(2, s_timeout);

        // Assert
        Assert.Equal(1, whileOpen);
        Assert.Equal([1L, -1L], connections.GetMeasurementSnapshot().Select(measurement => measurement.Value));
        Assert.Equal(0, Metrics.OpenConnections);
    }

    [Fact]
    public async Task EveryBroadcast_SendsOneMessagePerConnectedClient()
    {
        // Arrange: two phones listening.
        await WaitForOpenConnectionsAsync(0);
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await using RealtimeListener bensPhone = new(factory, ben.AccessToken);
        await adasPhone.StartAsync();
        await bensPhone.StartAsync();
        await WaitForOpenConnectionsAsync(2);
        using MetricCollector<long> broadcasts = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.broadcasts");
        using MetricCollector<long> messages = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.messages");
        using MetricCollector<long> itemChanges = new(MeterFactory, ApplicationMeter.Name, "nibbles.grocery.item_changes");

        // Act
        GroceryItemResponse added = await ada.AddItemAsync($"Tea {Guid.NewGuid():N}");
        await bensPhone.WaitForSavedAsync(added.Id, _ => true);
        await messages.WaitForMeasurementsAsync(1, s_timeout);

        // Assert: one broadcast of one saved item, which went to both phones.
        CollectedMeasurement<long> broadcast = Assert.Single(broadcasts.GetMeasurementSnapshot());
        Assert.Equal(1, broadcast.Value);
        Assert.Equal(GroceryListMetrics.ListChangedEvent, broadcast.Tags[GroceryListMetrics.EventTag]);

        CollectedMeasurement<long> sent = Assert.Single(messages.GetMeasurementSnapshot());
        Assert.Equal(2, sent.Value);
        Assert.Equal(GroceryListMetrics.ListChangedEvent, sent.Tags[GroceryListMetrics.EventTag]);

        CollectedMeasurement<long> saved = Assert.Single(itemChanges.GetMeasurementSnapshot());
        Assert.Equal(1, saved.Value);
        Assert.Equal(GroceryListMetrics.SavedChange, saved.Tags[GroceryListMetrics.ChangeTag]);
    }

    [Fact]
    public async Task ARefusedConnection_IsCountedByReason_AndNotAsConnected()
    {
        // Arrange
        await WaitForOpenConnectionsAsync(0);
        using MetricCollector<long> refused = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.connections.refused");
        using MetricCollector<long> connections = new(MeterFactory, ApplicationMeter.Name, "nibbles.signalr.connections");
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        await using RealtimeListener hijacked = new(factory, shopper.AccessToken, origin: "https://evil.example");

        // Act
        await hijacked.StartAsync();
        await hijacked.ClosedAsync();
        await refused.WaitForMeasurementsAsync(1, s_timeout);

        // Assert
        CollectedMeasurement<long> measurement = Assert.Single(refused.GetMeasurementSnapshot());
        Assert.Equal(GroceryListMetrics.RefusedCrossOrigin, measurement.Tags[GroceryListMetrics.ReasonTag]);
        Assert.Empty(connections.GetMeasurementSnapshot());
    }

    // The server learns of a closed connection a moment after the client, so a test that counts connections first waits
    // for the previous test's to be counted out.
    private async Task WaitForOpenConnectionsAsync(long expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_timeout);
        while (Metrics.OpenConnections != expected)
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
