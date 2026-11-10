using System.Diagnostics.CodeAnalysis;
using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Why a cost estimate came back, or didn't.
/// </summary>
internal enum CostEstimateStatus
{
    /// <summary>The estimate is in <see cref="CostEstimateResult.Estimate"/>.</summary>
    Succeeded,

    /// <summary>The server has no Gemini API key, or estimates are turned off.</summary>
    NotAvailable,

    /// <summary>There is nothing on the list to estimate.</summary>
    EmptyList,

    /// <summary>Today's allowance of estimates (<see cref="CostEstimateOptions.MaxEstimatesPerDay"/>) is used up.</summary>
    DailyLimitReached,

    /// <summary>Gemini is busy, rate-limited us or couldn't be reached: asking again shortly may well work.</summary>
    ModelBusy,

    /// <summary>Gemini refused the request, or its answer couldn't be used.</summary>
    ModelFailed,
}

/// <summary>
/// The outcome of <see cref="CostEstimateService.EstimateAsync"/>.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="Estimate">The estimate, when <paramref name="Status"/> is <see cref="CostEstimateStatus.Succeeded"/>.</param>
internal sealed record CostEstimateResult(CostEstimateStatus Status, CostEstimateResponse? Estimate)
{
    /// <summary>Gets a value indicating whether there is an estimate.</summary>
    [MemberNotNullWhen(true, nameof(Estimate))]
    public bool Succeeded => Status == CostEstimateStatus.Succeeded && Estimate is not null;

    /// <summary>An estimate.</summary>
    /// <param name="estimate">The estimate.</param>
    /// <returns>The result.</returns>
    public static CostEstimateResult Success(CostEstimateResponse estimate) => new(CostEstimateStatus.Succeeded, estimate);

    /// <summary>No estimate.</summary>
    /// <param name="status">Why not.</param>
    /// <returns>The result.</returns>
    public static CostEstimateResult Failure(CostEstimateStatus status) => new(status, null);
}
