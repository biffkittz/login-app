using LoginApp.Api.Features.GroceryList.Runs;
using Temporalio.Common;
using Temporalio.Worker;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Replays recorded histories of <see cref="GroceryRunWorkflow"/> against the current code, the way a new deployment
/// replays the runs in flight. A change that would make the workflow decide differently on an old history fails here
/// (non-determinism) instead of stalling real runs. Needs no Temporal server.
/// </summary>
/// <remarks>
/// The histories in <c>Histories/v1</c> were recorded from the workflow tests with <c>GROCERY_RUN_HISTORY_DIR</c> set
/// (docs/DEVELOPMENT.md). After an intentional change, guarded by <c>Workflow.Patched</c>, record the new behavior into
/// a new folder and keep the old ones: old histories are exactly what must keep replaying.
/// </remarks>
public sealed class GroceryRunReplayTests
{
    private static readonly string s_historyRoot = Path.Combine(AppContext.BaseDirectory, "Features", "GroceryList", "Runs", "Histories");

    /// <summary>Gets every recorded history, by its path below <c>Histories</c>.</summary>
    public static TheoryData<string> Histories => [.. Directory
        .EnumerateFiles(s_historyRoot, "*.json", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(s_historyRoot, path))
        .Order(StringComparer.Ordinal)];

    [Fact]
    public void ThereAreHistoriesToReplay() => Assert.True(Histories.Count >= 4);

    [Theory]
    [MemberData(nameof(Histories))]
    public async Task RecordedRunsReplayOnTheCurrentWorkflow(string history)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(s_historyRoot, history), TestContext.Current.CancellationToken);
        WorkflowReplayer replayer = new(new WorkflowReplayerOptions().AddWorkflow<GroceryRunWorkflow>());

        // Throws on a non-deterministic replay (the workflow task fails) instead of reporting it.
        WorkflowReplayResult result = await replayer.ReplayWorkflowAsync(
            WorkflowHistory.FromJson($"replay-{Path.GetFileNameWithoutExtension(history)}", json),
            throwOnReplayFailure: true,
            TestContext.Current.CancellationToken);
        Assert.Null(result.ReplayFailure);
    }
}
