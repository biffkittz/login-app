namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The state of a grocery run as <see cref="GroceryRunWorkflow"/> holds it: what its activities write to the database
/// and broadcast, and what its <c>State</c> query returns. IDs and times only (see
/// <see cref="GroceryRunWorkflowInput"/>).
/// </summary>
/// <param name="RunId">The run's ID.</param>
/// <param name="Sequence">Increases with every change. The database applies a state only if it is newer than the one
/// it has, which makes repeated and late activities harmless.</param>
/// <param name="Status">Where the run is.</param>
/// <param name="ShopperId">The user going shopping.</param>
/// <param name="ParticipantIds">The people asked during last call.</param>
/// <param name="ReadyUserIds">Those of them who said "I'm good", in the order they did.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="LastCallEndsAt">When last call ends at the latest.</param>
/// <param name="ShoppingStartedAt">When the shopping began, if it has.</param>
/// <param name="EndedAt">When the run ended, if it has.</param>
/// <param name="ExpiresAt">The latest moment the run can still be under way (see <see cref="GroceryRun.ExpiresAt"/>).</param>
/// <param name="LastCallOutcome">Why last call ended, once it has (and unless the run was cancelled).</param>
internal sealed record GroceryRunState(
    Guid RunId,
    int Sequence,
    GroceryRunStatus Status,
    string ShopperId,
    IReadOnlyList<string> ParticipantIds,
    IReadOnlyList<string> ReadyUserIds,
    DateTimeOffset StartedAt,
    DateTimeOffset LastCallEndsAt,
    DateTimeOffset? ShoppingStartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset ExpiresAt,
    LastCallOutcome? LastCallOutcome);
