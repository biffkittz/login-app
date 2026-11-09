using System.Threading.Channels;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// A realtime connection to <see cref="GroceryListHub"/> over the in-memory test server, recording every change it
/// receives. Connects the way the SPA does: WebSockets only, skipping negotiation.
/// </summary>
internal sealed class RealtimeListener : IAsyncDisposable
{
    private static readonly TimeSpan s_receiveTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<GroceryListChange> _changes = Channel.CreateUnbounded<GroceryListChange>();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="RealtimeListener"/> class. Call <see cref="StartAsync"/> next.
    /// </summary>
    /// <param name="factory">The API host.</param>
    /// <param name="accessToken">The token to authenticate with, or <see langword="null"/> for none.</param>
    /// <param name="origin">The <c>Origin</c> header a browser would send, or <see langword="null"/> for none.</param>
    public RealtimeListener(WebApplicationFactory<Program> factory, string? accessToken, string? origin = null)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl($"https://localhost{GroceryListHub.Path}", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    WebSocketClient client = factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        if (accessToken is not null)
                        {
                            request.Headers.Authorization = $"Bearer {accessToken}";
                        }

                        if (origin is not null)
                        {
                            request.Headers.Origin = origin;
                        }
                    };
                    return await client.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .Build();

        Connection.On<GroceryListChange>(nameof(IGroceryListClient.ListChanged), change => _changes.Writer.TryWrite(change));
        Connection.Closed += _ =>
        {
            _closed.TrySetResult();
            return Task.CompletedTask;
        };
    }

    /// <summary>Gets the underlying SignalR connection.</summary>
    public HubConnection Connection { get; }

    /// <summary>Opens the connection.</summary>
    /// <returns>A task that completes once connected.</returns>
    public Task StartAsync() => Connection.StartAsync(TestContext.Current.CancellationToken);

    /// <summary>Waits for the next change, failing the test after a timeout.</summary>
    /// <returns>The change.</returns>
    public async Task<GroceryListChange> NextChangeAsync() =>
        await _changes.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(s_receiveTimeout);

    /// <summary>Waits for the server to close the connection, failing the test after a timeout.</summary>
    /// <returns>A task that completes once the connection has closed.</returns>
    public Task ClosedAsync() => _closed.Task.WaitAsync(s_receiveTimeout, TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
