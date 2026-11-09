using System.Threading.Channels;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// A realtime connection to <see cref="GroceryListHub"/> over the in-memory test server, recording every change and
/// settings update it receives. Connects the way the SPA does: WebSockets only, skipping negotiation.
/// </summary>
internal sealed class RealtimeListener : IAsyncDisposable
{
    private static readonly TimeSpan s_receiveTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<GroceryListChange> _changes = Channel.CreateUnbounded<GroceryListChange>();
    private readonly Channel<GroceryListSettingsResponse> _settings = Channel.CreateUnbounded<GroceryListSettingsResponse>();
    private readonly List<GroceryListChange> _history = [];
    private readonly Lock _historyLock = new();
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

        Connection.On<GroceryListChange>(nameof(IGroceryListClient.ListChanged), change =>
        {
            lock (_historyLock)
            {
                _history.Add(change);
            }

            _changes.Writer.TryWrite(change);
        });
        Connection.On<GroceryListSettingsResponse>(
            nameof(IGroceryListClient.SettingsChanged),
            settings => _settings.Writer.TryWrite(settings));
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

    /// <summary>
    /// Waits until some change, received earlier or later, has saved <paramref name="itemId"/> in a state
    /// <paramref name="matches"/> accepts; fails the test after a timeout. Unlike <see cref="NextChangeAsync"/>, it
    /// doesn't consume changes, so waits for several items can be made in any order.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="matches">Recognizes the awaited state of the item.</param>
    /// <returns>The item as the latest such change saved it.</returns>
    public async Task<GroceryItemResponse> WaitForSavedAsync(Guid itemId, Func<GroceryItemResponse, bool> matches)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(s_receiveTimeout);
        while (true)
        {
            GroceryItemResponse? found;
            lock (_historyLock)
            {
                found = _history.SelectMany(change => change.SavedItems).LastOrDefault(item => item.Id == itemId && matches(item));
            }

            if (found is not null)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    /// <summary>Waits for the next settings update, failing the test after a timeout.</summary>
    /// <returns>The settings.</returns>
    public async Task<GroceryListSettingsResponse> NextSettingsAsync() =>
        await _settings.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(s_receiveTimeout);

    /// <summary>Waits for the server to close the connection, failing the test after a timeout.</summary>
    /// <returns>A task that completes once the connection has closed.</returns>
    public Task ClosedAsync() => _closed.Task.WaitAsync(s_receiveTimeout, TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
