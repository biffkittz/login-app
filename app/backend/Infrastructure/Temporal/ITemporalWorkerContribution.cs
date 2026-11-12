using Temporalio.Worker;

namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// Adds a feature's workflows and activities to the app's Temporal worker. Features register an implementation as a
/// singleton; <see cref="TemporalWorkerService"/> applies every one, so the infrastructure needs no knowledge of the
/// features.
/// </summary>
internal interface ITemporalWorkerContribution
{
    /// <summary>
    /// Adds the feature's workflows and activities to the worker about to start.
    /// </summary>
    /// <param name="options">The worker's options.</param>
    public void AddTo(TemporalWorkerOptions options);
}
