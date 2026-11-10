using System.Collections.Concurrent;
using LoginApp.Api.Features.GroceryList.CostEstimates;

namespace LoginApp.Api.Tests.Features.GroceryList.CostEstimates;

/// <summary>
/// Stands in for Gemini: records each list it was asked to price and answers with a fixed price per item, or whatever
/// a test tells it to.
/// </summary>
internal sealed class FakeCostEstimator : ICostEstimator
{
    /// <summary>What the default answer charges for every item.</summary>
    public const decimal PricePerItem = 2.5m;

    private readonly ConcurrentQueue<IReadOnlyList<CostEstimateItem>> _requests = new();

    /// <summary>
    /// Gets or sets how the next requests are answered. Tests that change it put the default back when done, because
    /// the factory (and so this instance) is shared by a test class.
    /// </summary>
    public Func<IReadOnlyList<CostEstimateItem>, CancellationToken, Task<ModelCostEstimate>> Answer { get; set; } = PriceAsync;

    /// <summary>Gets every list asked about so far, in order.</summary>
    public IReadOnlyCollection<IReadOnlyList<CostEstimateItem>> Requests => _requests;

    /// <summary>The default answer: <see cref="PricePerItem"/> for every item.</summary>
    /// <param name="items">The list.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>The estimate.</returns>
    public static Task<ModelCostEstimate> PriceAsync(IReadOnlyList<CostEstimateItem> items, CancellationToken cancellationToken) =>
        Task.FromResult(new ModelCostEstimate(
            [.. items.Select(item => new ModelCostLine(item.ItemNumber, PricePerItem, $"Basis {item.ItemNumber}"))],
            "National averages."));

    /// <inheritdoc />
    public Task<ModelCostEstimate> EstimateAsync(IReadOnlyList<CostEstimateItem> items, CancellationToken cancellationToken)
    {
        _requests.Enqueue(items);
        return Answer(items, cancellationToken);
    }
}
