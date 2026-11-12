namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// What <see cref="GroceryRunWorkflow"/> starts with.
/// </summary>
/// <remarks>
/// Temporal Cloud keeps workflow inputs, signals and activity arguments in its history, so they carry opaque IDs and
/// times only: never a name, an email address or an item.
/// </remarks>
/// <param name="RunId">The run's ID, which its database row will have.</param>
/// <param name="ShopperId">The user going shopping, who started the run.</param>
/// <param name="ParticipantIds">Everyone else who had the list open when the run started: the people last call waits
/// for. May be empty, and then last call lasts its full time unless the shopper goes early.</param>
/// <param name="LastCallWindow">How long last call lasts at most.</param>
/// <param name="ShoppingTimeLimit">How long the shopping may last before the run ends by itself.</param>
internal sealed record GroceryRunWorkflowInput(
    Guid RunId,
    string ShopperId,
    IReadOnlyList<string> ParticipantIds,
    TimeSpan LastCallWindow,
    TimeSpan ShoppingTimeLimit);
