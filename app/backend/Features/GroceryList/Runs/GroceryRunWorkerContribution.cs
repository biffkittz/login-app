using LoginApp.Api.Infrastructure.Temporal;
using Temporalio.Worker;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Puts the grocery run workflow and its activities on the app's Temporal worker.
/// </summary>
/// <param name="activities">The activities, one instance for the worker's lifetime.</param>
internal sealed class GroceryRunWorkerContribution(GroceryRunActivities activities) : ITemporalWorkerContribution
{
    /// <inheritdoc />
    public void AddTo(TemporalWorkerOptions options) =>
        options.AddWorkflow<GroceryRunWorkflow>().AddAllActivities(activities);
}
