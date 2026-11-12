using Microsoft.AspNetCore.Hosting;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="LoginApiFactory"/> configured for Temporal, so the app runs its real Temporal worker in-process: against
/// the development server (<see cref="Temporal.TemporalDevServer"/>), or against an address where nothing answers.
/// </summary>
/// <param name="address">The Temporal frontend's address.</param>
/// <param name="taskQueue">The site's task queue; unique per test, so tests never take each other's tasks.</param>
public sealed class TemporalApiFactory(string address, string taskQueue) : LoginApiFactory
{
    /// <summary>Gets the site's task queue.</summary>
    public string TaskQueue => taskQueue;

    /// <summary>
    /// Creates the host and its database. The caller disposes it.
    /// </summary>
    /// <param name="address">The Temporal frontend's address.</param>
    /// <returns>The initialized factory, with a task queue of its own.</returns>
    public static async Task<TemporalApiFactory> CreateAsync(string address)
    {
        TemporalApiFactory factory = new(address, $"nibbles-tests-{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        return factory;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Temporal:Address", address);
        builder.UseSetting("Temporal:Namespace", "default");
        builder.UseSetting("Temporal:TaskQueue", taskQueue);
    }
}
