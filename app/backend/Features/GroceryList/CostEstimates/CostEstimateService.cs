using System.Globalization;
using System.Text;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Estimates what the whole grocery list costs: reads the list, spends one of the day's estimates, asks the model and
/// matches its answer back to the items.
/// </summary>
/// <param name="dbContext">The application database.</param>
/// <param name="estimator">The model.</param>
/// <param name="budget">The day's allowance of estimates.</param>
/// <param name="options">Whether estimates are turned on.</param>
/// <param name="geminiOptions">Whether there is a Gemini API key.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed class CostEstimateService(
    ApplicationDbContext dbContext,
    ICostEstimator estimator,
    CostEstimateBudget budget,
    IOptions<CostEstimateOptions> options,
    IOptions<ImageGenerationOptions> geminiOptions,
    TimeProvider timeProvider,
    ILogger<CostEstimateService> logger)
{
    /// <summary>The longest <see cref="CostEstimateLineResponse.Basis"/> shown; the model is asked for 80 characters.</summary>
    internal const int BasisMaxLength = 100;

    /// <summary>The longest <see cref="CostEstimateResponse.Note"/> shown; the model is asked for 200 characters.</summary>
    internal const int NoteMaxLength = 300;

    // No single grocery item costs more than this. A larger figure is the model getting it wrong (pricing cents as
    // dollars, say), and would swamp the total, so the item is shown without a price instead.
    private const decimal MaxItemCostUsd = 10_000m;

    /// <summary>
    /// Estimates the cost of every item on the list, at average U.S. prices.
    /// </summary>
    /// <param name="userId">The signed-in user asking, for the log.</param>
    /// <param name="cancellationToken">Cancels the estimate, e.g. when the user leaves.</param>
    /// <returns>The estimate, or why there is none.</returns>
    public async Task<CostEstimateResult> EstimateAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.IsAvailable(geminiOptions.Value))
        {
            return CostEstimateResult.Failure(CostEstimateStatus.NotAvailable);
        }

        var items = await dbContext.GroceryItems
            .AsNoTracking()
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.Name, item.Quantity, item.Brand })
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return CostEstimateResult.Failure(CostEstimateStatus.EmptyList);
        }

        // Spent before asking, so a request that fails counts too: it costs the same.
        if (!budget.TryTake())
        {
            CostEstimateLog.DailyLimitReached(logger, userId, budget.Limit);
            return CostEstimateResult.Failure(CostEstimateStatus.DailyLimitReached);
        }

        CostEstimateItem[] request = [.. items.Select((item, index) => new CostEstimateItem(index + 1, item.Name, item.Quantity, item.Brand))];
        long started = timeProvider.GetTimestamp();
        ModelCostEstimate answer;
        try
        {
            answer = await estimator.EstimateAsync(request, cancellationToken);
        }
        catch (CostEstimateException exception)
        {
            CostEstimateLog.Failed(logger, userId, exception.Message);
            return CostEstimateResult.Failure(exception.IsTransient ? CostEstimateStatus.ModelBusy : CostEstimateStatus.ModelFailed);
        }
        catch (HttpRequestException)
        {
            CostEstimateLog.Failed(logger, userId, "Gemini couldn't be reached");
            return CostEstimateResult.Failure(CostEstimateStatus.ModelBusy);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HTTP client's timeout, not the user leaving.
            CostEstimateLog.Failed(logger, userId, "Gemini didn't answer in time");
            return CostEstimateResult.Failure(CostEstimateStatus.ModelBusy);
        }

        // The first answer for each item number counts; numbers that aren't on the list are ignored.
        Dictionary<int, ModelCostLine> lines = [];
        foreach (ModelCostLine line in answer.Items)
        {
            lines.TryAdd(line.ItemNumber, line);
        }

        List<CostEstimateLineResponse> estimates = new(items.Count);
        int matched = 0;
        foreach ((var item, int index) in items.Select((item, index) => (item, index)))
        {
            ModelCostLine? line = lines.GetValueOrDefault(index + 1);
            matched += line is null ? 0 : 1;
            estimates.Add(new CostEstimateLineResponse(
                item.Id,
                item.Name,
                item.Quantity,
                ToDollars(line?.EstimatedCostUsd),
                Clean(line?.Basis, BasisMaxLength)));
        }

        if (matched == 0)
        {
            CostEstimateLog.NothingMatched(logger, userId, items.Count);
            return CostEstimateResult.Failure(CostEstimateStatus.ModelFailed);
        }

        int estimatedItemCount = estimates.Count(line => line.EstimatedUsd is not null);
        long elapsedMilliseconds = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        CostEstimateLog.Estimated(logger, userId, items.Count, estimatedItemCount, elapsedMilliseconds);
        return CostEstimateResult.Success(new CostEstimateResponse(
            estimates.Sum(line => line.EstimatedUsd ?? 0m),
            estimatedItemCount,
            estimates,
            Clean(answer.Note, NoteMaxLength),
            timeProvider.GetUtcNow()));
    }

    /// <summary>
    /// Makes model-written text safe to show as one line: control and formatting characters (line breaks, bidi
    /// overrides, zero-width characters) go, runs of white space become one space, and long text is cut.
    /// </summary>
    /// <param name="text">The model's text.</param>
    /// <param name="maxLength">The most characters to keep.</param>
    /// <returns>The text, or <see langword="null"/> when nothing is left.</returns>
    /// <remarks>
    /// The SPA shows it as text, never HTML, so this isn't about markup: a bidi override could make a price read
    /// backwards, and a line break would break the layout.
    /// </remarks>
    internal static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        StringBuilder cleaned = new(Math.Min(text.Length, maxLength));
        bool pendingSpace = false;
        foreach (char character in text)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (char.IsWhiteSpace(character) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = cleaned.Length > 0;
                continue;
            }

            if (char.IsControl(character) || category is UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                cleaned.Append(' ');
                pendingSpace = false;
            }

            cleaned.Append(character);
        }

        if (cleaned.Length > maxLength)
        {
            // Don't split a surrogate pair (an emoji, say) in two.
            int length = char.IsHighSurrogate(cleaned[maxLength - 1]) ? maxLength - 1 : maxLength;
            cleaned.Length = length;
        }

        string result = cleaned.ToString().TrimEnd();
        return result.Length > 0 ? result : null;
    }

    // Whole cents; an implausible price counts as no price.
    private static decimal? ToDollars(decimal? cost) =>
        cost is decimal value and >= 0 and <= MaxItemCostUsd
            ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
}
