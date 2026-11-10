namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>POST /api/grocery-list/cost-estimate</c>: what the list would cost at average U.S. prices, as Gemini
/// estimates them.
/// </summary>
/// <param name="TotalUsd">The sum of the items' estimates, in dollars. Items without an estimate add nothing.</param>
/// <param name="EstimatedItemCount">How many items have an estimate.</param>
/// <param name="Items">Every item on the list, in list order, with its estimate.</param>
/// <param name="Note">The model's one-sentence remark on the estimate, if it made one.</param>
/// <param name="EstimatedAt">When the estimate was made.</param>
public sealed record CostEstimateResponse(
    decimal TotalUsd,
    int EstimatedItemCount,
    IReadOnlyList<CostEstimateLineResponse> Items,
    string? Note,
    DateTimeOffset EstimatedAt);

/// <summary>
/// One item of a <see cref="CostEstimateResponse"/>.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Name">The item's name, from the list (not as the model repeated it).</param>
/// <param name="Quantity">The item's quantity, from the list.</param>
/// <param name="EstimatedUsd">The average U.S. price of that quantity, in dollars, or <see langword="null"/> when the
/// model couldn't price it.</param>
/// <param name="Basis">What the model priced, in a few words ("1 gallon, store brand").</param>
public sealed record CostEstimateLineResponse(Guid ItemId, string Name, string? Quantity, decimal? EstimatedUsd, string? Basis);
