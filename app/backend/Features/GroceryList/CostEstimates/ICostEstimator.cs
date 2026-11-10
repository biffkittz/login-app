namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Asks an AI model what a grocery list costs. Implemented by <see cref="GeminiCostEstimator"/>; tests swap in a
/// stand-in, so nothing in the test suite calls a paid API.
/// </summary>
internal interface ICostEstimator
{
    /// <summary>
    /// Asks for the average U.S. price of every item on the list.
    /// </summary>
    /// <param name="items">The list, numbered from 1.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The model's answer, checked against the response schema but not yet matched to the list.</returns>
    /// <exception cref="CostEstimateException">The API refused the request, or its answer wasn't the JSON asked for.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached.</exception>
    public Task<ModelCostEstimate> EstimateAsync(IReadOnlyList<CostEstimateItem> items, CancellationToken cancellationToken);
}

/// <summary>
/// One list item as the model sees it: what to buy, how much and which brand. Nothing else about the item, or about who
/// added it, leaves the app.
/// </summary>
/// <param name="ItemNumber">The item's number on the list sent, from 1; the answer refers to items by it.</param>
/// <param name="Name">What to buy.</param>
/// <param name="Quantity">How much, as the user wrote it.</param>
/// <param name="Brand">A preferred brand.</param>
internal sealed record CostEstimateItem(int ItemNumber, string Name, string? Quantity, string? Brand);

/// <summary>
/// The model's answer, in the shape of <see cref="GeminiCostEstimator.ResponseSchema"/>.
/// </summary>
/// <param name="Items">The model's estimate for each item it priced.</param>
/// <param name="Note">The model's one-sentence remark on the estimate as a whole.</param>
internal sealed record ModelCostEstimate(IReadOnlyList<ModelCostLine> Items, string? Note);

/// <summary>
/// The model's estimate for one item.
/// </summary>
/// <param name="ItemNumber">Which item, by <see cref="CostEstimateItem.ItemNumber"/>.</param>
/// <param name="EstimatedCostUsd">The national average price in dollars, or <see langword="null"/> when the model
/// couldn't price it.</param>
/// <param name="Basis">What the model priced, in a few words ("1 gallon, store brand").</param>
internal sealed record ModelCostLine(int ItemNumber, decimal? EstimatedCostUsd, string? Basis);
