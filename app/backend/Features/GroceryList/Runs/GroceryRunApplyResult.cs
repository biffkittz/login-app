using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// What <see cref="GroceryRunStore.ApplyAsync"/> did.
/// </summary>
/// <param name="Run">The run as it now stands, to broadcast.</param>
/// <param name="Applied">Whether the state was new to the database. A repeated activity finds it saved already.</param>
/// <param name="PreviousStatus">The run's status before, or <see langword="null"/> if the run was new.</param>
/// <param name="SupersededRunIds">Earlier runs that were still marked as under way, and have now ended.</param>
internal sealed record GroceryRunApplyResult(
    GroceryRunResponse Run,
    bool Applied,
    GroceryRunStatus? PreviousStatus,
    IReadOnlyList<Guid> SupersededRunIds);
