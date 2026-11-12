using System.Diagnostics.Metrics;
using LoginApp.Api.Infrastructure.Telemetry;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Grocery runs' metrics: how runs begin and end, and why last call ended. Recorded where a new state reaches the
/// database, so a retried activity doesn't count twice. The Temporal SDK's own metrics (<c>temporal_*</c>) cover the
/// worker.
/// </summary>
internal sealed class GroceryRunMetrics
{
    /// <summary>The attribute naming how a run ended.</summary>
    public const string StatusTag = "status";

    /// <summary>The attribute naming why last call ended.</summary>
    public const string OutcomeTag = "outcome";

    private readonly Counter<long> _started;
    private readonly Counter<long> _lastCalls;
    private readonly Counter<long> _ended;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroceryRunMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public GroceryRunMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        _started = meter.CreateCounter<long>(
            "nibbles.grocery_runs.started",
            "{run}",
            "Grocery runs that opened last call.");
        _lastCalls = meter.CreateCounter<long>(
            "nibbles.grocery_runs.last_calls",
            "{run}",
            "Last calls that ended and let the shopping begin, by why they ended.");
        _ended = meter.CreateCounter<long>(
            "nibbles.grocery_runs.ended",
            "{run}",
            "Grocery runs that ended, by how.");
    }

    /// <summary>Records that a run opened last call.</summary>
    public void Started() => _started.Add(1);

    /// <summary>Records that a run's last call ended and the shopping began.</summary>
    /// <param name="outcome">Why last call ended.</param>
    public void LastCallEnded(LastCallOutcome outcome) =>
        _lastCalls.Add(1, new KeyValuePair<string, object?>(OutcomeTag, ToTagValue(outcome)));

    /// <summary>Records that a run ended.</summary>
    /// <param name="status">How: finished, expired or cancelled.</param>
    public void Ended(GroceryRunStatus status) =>
        _ended.Add(1, new KeyValuePair<string, object?>(StatusTag, ToTagValue(status)));

    /// <summary>Returns the tag value for a last call outcome: fixed, snake-case strings.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The tag value.</returns>
    public static string ToTagValue(LastCallOutcome outcome) => outcome switch
    {
        LastCallOutcome.EveryoneReady => "everyone_ready",
        LastCallOutcome.TimeUp => "time_up",
        LastCallOutcome.ShopperWentEarly => "shopper_went_early",
        _ => "other",
    };

    /// <summary>Returns the tag value for a run's status: fixed, snake-case strings.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The tag value.</returns>
    public static string ToTagValue(GroceryRunStatus status) => status switch
    {
        GroceryRunStatus.LastCall => "last_call",
        GroceryRunStatus.Shopping => "shopping",
        GroceryRunStatus.Finished => "finished",
        GroceryRunStatus.Expired => "expired",
        GroceryRunStatus.Cancelled => "cancelled",
        _ => "other",
    };
}
