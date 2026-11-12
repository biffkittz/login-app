namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The argument of every signal to <see cref="GroceryRunWorkflow"/>: who sent it, for which run.
/// </summary>
/// <remarks>
/// Every run of a site has the same workflow ID, so a signal names its run too: a tap on a banner that was already out
/// of date can't count towards the next run.
/// </remarks>
/// <param name="RunId">The run the signal is meant for.</param>
/// <param name="UserId">The signed-in user who sent it.</param>
internal sealed record GroceryRunSignal(Guid RunId, string UserId);
