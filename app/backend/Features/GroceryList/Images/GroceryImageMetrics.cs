using System.Diagnostics.Metrics;
using LoginApp.Api.Infrastructure.Telemetry;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// What became of the pictures items waited for (<c>nibbles.pictures</c>, by outcome) and how long drawing one took
/// (<c>nibbles.pictures.duration</c>). Drawn pictures are the ones Gemini charged for; together with the daily
/// limit's refusals they show how close the app runs to <see cref="ImageGenerationOptions.MaxImagesPerDay"/>.
/// </summary>
internal sealed class GroceryImageMetrics
{
    /// <summary>The attribute naming the outcome.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>A new picture was drawn and saved.</summary>
    public const string Drawn = "drawn";

    /// <summary>The picture already drawn for the same name was used.</summary>
    public const string Reused = "reused";

    /// <summary>Gemini refused or failed, or the picture couldn't be shrunk.</summary>
    public const string Failed = "failed";

    /// <summary>The day's allowance was used up, so none was asked for.</summary>
    public const string DailyLimit = "daily_limit";

    private readonly Counter<long> _pictures;
    private readonly Histogram<double> _duration;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroceryImageMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public GroceryImageMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        _pictures = meter.CreateCounter<long>(
            "nibbles.pictures",
            "{picture}",
            "Pictures items waited for, by outcome.");

        // Gemini usually draws a picture in a few seconds; retries can stretch that past half a minute.
        _duration = meter.CreateHistogram(
            "nibbles.pictures.duration",
            "s",
            "Time to draw, shrink and save a new picture, retries included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 4, 8, 15, 30, 60] });
    }

    /// <summary>Records a newly drawn picture.</summary>
    /// <param name="elapsed">How long drawing, shrinking and saving it took.</param>
    public void PictureDrawn(TimeSpan elapsed)
    {
        _pictures.Add(1, new KeyValuePair<string, object?>(OutcomeTag, Drawn));
        _duration.Record(elapsed.TotalSeconds);
    }

    /// <summary>Records an outcome other than a new picture.</summary>
    /// <param name="outcome"><see cref="Reused"/>, <see cref="Failed"/> or <see cref="DailyLimit"/>.</param>
    public void PictureNotDrawn(string outcome) => _pictures.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));
}
