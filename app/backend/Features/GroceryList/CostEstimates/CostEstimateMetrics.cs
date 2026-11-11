using System.Diagnostics.Metrics;
using LoginApp.Api.Infrastructure.Telemetry;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Every press of Estimate Cost by outcome (<c>nibbles.cost_estimates</c>) and how long Gemini took to answer
/// (<c>nibbles.cost_estimates.duration</c>). The outcomes that reached Gemini are what
/// <see cref="CostEstimateOptions.MaxEstimatesPerDay"/> limits; <c>daily_limit_reached</c> shows when it bit.
/// </summary>
internal sealed class CostEstimateMetrics
{
    /// <summary>The attribute naming the outcome, a <see cref="CostEstimateStatus"/> in snake case.</summary>
    public const string StatusTag = "status";

    private readonly Counter<long> _estimates;
    private readonly Histogram<double> _duration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostEstimateMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public CostEstimateMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        _estimates = meter.CreateCounter<long>(
            "nibbles.cost_estimates",
            "{estimate}",
            "Cost estimates asked for, by outcome.");

        // Flash-Lite answers a list in a second or two; the HTTP client gives up after 60 seconds.
        _duration = meter.CreateHistogram(
            "nibbles.cost_estimates.duration",
            "s",
            "Time Gemini took to answer a cost estimate, failed answers included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.25, 0.5, 1, 2, 4, 8, 15, 30, 60] });
    }

    /// <summary>Records how an estimate ended.</summary>
    /// <param name="status">The outcome.</param>
    public void EstimateFinished(CostEstimateStatus status) =>
        _estimates.Add(1, new KeyValuePair<string, object?>(StatusTag, ToTagValue(status)));

    /// <summary>Records how long Gemini took to answer (or to fail).</summary>
    /// <param name="elapsed">The time between asking and the answer.</param>
    public void ModelAnswered(TimeSpan elapsed) => _duration.Record(elapsed.TotalSeconds);

    /// <summary>Spells a status as a metric attribute value.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The status in snake case, e.g. <c>daily_limit_reached</c>.</returns>
    internal static string ToTagValue(CostEstimateStatus status) => status switch
    {
        CostEstimateStatus.Succeeded => "succeeded",
        CostEstimateStatus.NotAvailable => "not_available",
        CostEstimateStatus.EmptyList => "empty_list",
        CostEstimateStatus.DailyLimitReached => "daily_limit_reached",
        CostEstimateStatus.ModelBusy => "model_busy",
        CostEstimateStatus.ModelFailed => "model_failed",
        _ => "unknown",
    };
}
