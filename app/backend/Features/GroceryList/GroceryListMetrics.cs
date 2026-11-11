using System.Diagnostics.Metrics;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Infrastructure.Telemetry;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The realtime list's metrics: who is connected to <see cref="GroceryListHub"/> and what it pushes to them.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core measures SignalR connections itself (<c>signalr.server.active_connections</c>), counting every
/// WebSocket the moment it opens. <c>nibbles.signalr.connections</c> counts only the clients the hub accepted, which is
/// the number of phones that actually receive the list's changes.
/// </para>
/// <para>
/// Every saved change is one broadcast (<c>nibbles.signalr.broadcasts</c>) and one message to each connected client
/// (<c>nibbles.signalr.messages</c>), so the rate of the second is the hub's messages per second. The count of
/// recipients is taken just before sending: a client that connects during a send may or may not get that message.
/// </para>
/// </remarks>
internal sealed class GroceryListMetrics
{
    /// <summary>The attribute naming why a connection was refused.</summary>
    public const string ReasonTag = "reason";

    /// <summary>The attribute naming the message a broadcast sent.</summary>
    public const string EventTag = "event";

    /// <summary>The attribute telling saved items from deleted ones.</summary>
    public const string ChangeTag = "change";

    /// <summary>A connection opened by a page on another site.</summary>
    public const string RefusedCrossOrigin = "cross_origin";

    /// <summary>A connection for an account that doesn't exist (any more).</summary>
    public const string RefusedUnknownUser = "unknown_user";

    /// <summary>The <see cref="IGroceryListClient.ListChanged"/> message.</summary>
    public const string ListChangedEvent = "list_changed";

    /// <summary>The <see cref="IGroceryListClient.SettingsChanged"/> message.</summary>
    public const string SettingsChangedEvent = "settings_changed";

    /// <summary>Items created or changed.</summary>
    public const string SavedChange = "saved";

    /// <summary>Items deleted.</summary>
    public const string DeletedChange = "deleted";

    private readonly UpDownCounter<long> _connections;
    private readonly Counter<long> _refusedConnections;
    private readonly Counter<long> _broadcasts;
    private readonly Counter<long> _messages;
    private readonly Counter<long> _itemChanges;
    private long _openConnections;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroceryListMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public GroceryListMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        _connections = meter.CreateUpDownCounter<long>(
            "nibbles.signalr.connections",
            "{connection}",
            "Realtime connections the grocery list hub accepted that are still open.");
        _refusedConnections = meter.CreateCounter<long>(
            "nibbles.signalr.connections.refused",
            "{connection}",
            "Realtime connections the hub closed at once, by reason.");
        _broadcasts = meter.CreateCounter<long>(
            "nibbles.signalr.broadcasts",
            "{broadcast}",
            "Changes the hub pushed to every connected client, by message.");
        _messages = meter.CreateCounter<long>(
            "nibbles.signalr.messages",
            "{message}",
            "Messages the hub sent: one per connected client for each broadcast.");
        _itemChanges = meter.CreateCounter<long>(
            "nibbles.grocery.item_changes",
            "{item}",
            "Grocery items in the changes the hub pushed, saved or deleted.");
    }

    /// <summary>Gets the number of accepted connections that are still open.</summary>
    public long OpenConnections => Interlocked.Read(ref _openConnections);

    /// <summary>Records that the hub accepted a connection.</summary>
    public void ConnectionAccepted()
    {
        Interlocked.Increment(ref _openConnections);
        _connections.Add(1);
    }

    /// <summary>Records that an accepted connection closed.</summary>
    public void ConnectionClosed()
    {
        Interlocked.Decrement(ref _openConnections);
        _connections.Add(-1);
    }

    /// <summary>Records that the hub refused a connection.</summary>
    /// <param name="reason"><see cref="RefusedCrossOrigin"/> or <see cref="RefusedUnknownUser"/>.</param>
    public void ConnectionRefused(string reason) =>
        _refusedConnections.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));

    /// <summary>Records a broadcast of a change to the list.</summary>
    /// <param name="change">The change.</param>
    /// <param name="recipients">The connections it went to.</param>
    public void ListChangeBroadcast(GroceryListChange change, long recipients)
    {
        Broadcast(ListChangedEvent, recipients);
        if (change.SavedItems.Count > 0)
        {
            _itemChanges.Add(change.SavedItems.Count, new KeyValuePair<string, object?>(ChangeTag, SavedChange));
        }

        if (change.DeletedItemIds.Count > 0)
        {
            _itemChanges.Add(change.DeletedItemIds.Count, new KeyValuePair<string, object?>(ChangeTag, DeletedChange));
        }
    }

    /// <summary>Records a broadcast of the list's settings.</summary>
    /// <param name="recipients">The connections it went to.</param>
    public void SettingsChangeBroadcast(long recipients) => Broadcast(SettingsChangedEvent, recipients);

    private void Broadcast(string message, long recipients)
    {
        KeyValuePair<string, object?> tag = new(EventTag, message);
        _broadcasts.Add(1, tag);
        if (recipients > 0)
        {
            _messages.Add(recipients, tag);
        }
    }
}
