using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Tests.Infrastructure.Temporal;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;

namespace LoginApp.Api.Tests.Features.GroceryList.Runs;

/// <summary>
/// Runs one <see cref="GroceryRunWorkflow"/> on the development server, on a task queue of its own with a worker whose
/// activities only record what the workflow publishes (<see cref="RecordingRunActivities"/>).
/// </summary>
internal sealed class WorkflowHarness : IAsyncDisposable
{
    /// <summary>The shopper's user ID in workflow tests.</summary>
    public const string ShopperId = "shopper-0000";

    // Where the workflow tests save the histories they produce, when set: how the recorded histories that
    // GroceryRunReplayTests replays were made (docs/DEVELOPMENT.md).
    private const string HistoryDirectoryVariable = "GROCERY_RUN_HISTORY_DIR";

    private readonly CancellationTokenSource _stopWorker = new();
    private readonly Task _worker;

    private WorkflowHarness(WorkflowEnvironment environment, GroceryRunWorkflowInput input, string taskQueue)
    {
        Input = input;
        TaskQueue = taskQueue;
        Client = environment.Client;
        TemporalWorker worker = new(
            environment.Client,
            new TemporalWorkerOptions(taskQueue).AddWorkflow<GroceryRunWorkflow>().AddAllActivities(Activities));
        _worker = RunWorkerAsync(worker, _stopWorker.Token);
    }

    /// <summary>Gets the stand-in activities, which record what the workflow publishes.</summary>
    public RecordingRunActivities Activities { get; } = new();

    /// <summary>Gets the run's input.</summary>
    public GroceryRunWorkflowInput Input { get; }

    /// <summary>Gets the run's task queue.</summary>
    public string TaskQueue { get; }

    /// <summary>Gets the client connected to the development server.</summary>
    public ITemporalClient Client { get; }

    /// <summary>Gets the handle of the running workflow.</summary>
    public WorkflowHandle<GroceryRunWorkflow, GroceryRunStatus> Handle { get; private set; } = null!;

    /// <summary>
    /// Starts a worker and a run.
    /// </summary>
    /// <param name="server">The development server.</param>
    /// <param name="participantIds">The people last call asks.</param>
    /// <param name="lastCall">How long last call lasts at most.</param>
    /// <param name="shoppingTimeLimit">How long the shopping may last.</param>
    /// <returns>The harness, with the workflow started.</returns>
    public static async Task<WorkflowHarness> StartAsync(
        TemporalDevServer server,
        IReadOnlyList<string> participantIds,
        TimeSpan lastCall,
        TimeSpan? shoppingTimeLimit = null)
    {
        WorkflowEnvironment environment = await server.GetAsync();
        GroceryRunWorkflowInput input = new(Guid.NewGuid(), ShopperId, participantIds, lastCall, shoppingTimeLimit ?? TimeSpan.FromHours(4));
        WorkflowHarness harness = new(environment, input, $"grocery-run-tests-{Guid.NewGuid():N}");
        harness.Handle = await harness.Client.StartWorkflowAsync(
            (GroceryRunWorkflow workflow) => workflow.RunAsync(input),
            new WorkflowOptions($"grocery-run-test-{input.RunId:N}", harness.TaskQueue));
        return harness;
    }

    /// <summary>Sends a signal to the run.</summary>
    /// <param name="signal">The signal, as a call on the workflow.</param>
    /// <returns>A task that completes when the server has the signal.</returns>
    public Task SignalAsync(Expression<Func<GroceryRunWorkflow, Task>> signal) => Handle.SignalAsync(signal);

    /// <summary>Creates the argument of a signal from <paramref name="userId"/> for this run.</summary>
    /// <param name="userId">The sender.</param>
    /// <returns>The argument.</returns>
    public GroceryRunSignal From(string userId) => new(Input.RunId, userId);

    /// <summary>
    /// Saves the finished workflow's history as <c>{name}.json</c> in <c>GROCERY_RUN_HISTORY_DIR</c>, when that is set.
    /// </summary>
    /// <param name="name">The scenario's file name, without extension.</param>
    /// <returns>A task that completes when the history is saved (or at once).</returns>
    public async Task SaveHistoryAsync(string name)
    {
        string? directory = Environment.GetEnvironmentVariable(HistoryDirectoryVariable);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Temporalio.Common.WorkflowHistory history = await Handle.FetchHistoryAsync();
        JsonNode json = JsonNode.Parse(history.ToJson())!;
        HashSet<string> identities = new(StringComparer.Ordinal);
        CollectIdentities(json, identities);
        ScrubIdentities(json, identities);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{name}.json"),
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopWorker.CancelAsync();
        await _worker;
        _stopWorker.Dispose();
    }

    // Histories are committed to a public repository. A worker's identity is "<process id>@<host name>", and its sticky
    // task queue's name starts with it: replace it wherever it occurs, as replaying reads neither.
    private static void CollectIdentities(JsonNode? node, HashSet<string> identities)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach ((string key, JsonNode? value) in jsonObject)
                {
                    if (key == "identity" && value is JsonValue identity && identity.GetValue<string>() is { Length: > 0 } text)
                    {
                        identities.Add(text);
                    }

                    CollectIdentities(value, identities);
                }

                break;
            case JsonArray jsonArray:
                foreach (JsonNode? element in jsonArray)
                {
                    CollectIdentities(element, identities);
                }

                break;
        }
    }

    private static void ScrubIdentities(JsonNode? node, HashSet<string> identities)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach ((string key, JsonNode? value) in jsonObject.ToList())
                {
                    if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text))
                    {
                        jsonObject[key] = identities.Aggregate(text, (scrubbed, identity) => scrubbed.Replace(identity, "grocery-run-tests", StringComparison.Ordinal));
                    }
                    else
                    {
                        ScrubIdentities(value, identities);
                    }
                }

                break;
            case JsonArray jsonArray:
                foreach (JsonNode? element in jsonArray)
                {
                    ScrubIdentities(element, identities);
                }

                break;
        }
    }

    private static async Task RunWorkerAsync(TemporalWorker worker, CancellationToken stoppingToken)
    {
        using (worker)
        {
            try
            {
                await worker.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopped by DisposeAsync.
            }
        }
    }
}
