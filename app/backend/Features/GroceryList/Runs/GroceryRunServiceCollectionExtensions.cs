using LoginApp.Api.Infrastructure.Temporal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Registers grocery runs: the API's service, the database copy of the runs, the workflow's activities and, when
/// Temporal is configured, the coordinator that starts and signals workflows.
/// </summary>
internal static class GroceryRunServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything grocery runs need. Without Temporal (no <c>Temporal:Address</c>), runs are unavailable and the
    /// app says so; the rest of the list is unaffected.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">The app's configuration, to tell whether Temporal is configured.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGroceryRuns(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GroceryRunOptions>()
            .BindConfiguration(GroceryRunOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<GroceryListPresence>();
        services.AddSingleton<GroceryRunMetrics>();
        services.AddScoped<GroceryRunStore>();
        services.AddScoped<GroceryRunService>();

        TemporalOptions temporal = configuration.GetSection(TemporalOptions.SectionName).Get<TemporalOptions>() ?? new();
        if (temporal.IsConfigured)
        {
            services.AddSingleton<IGroceryRunCoordinator, TemporalGroceryRunCoordinator>();
            services.AddSingleton<GroceryRunActivities>();
            services.AddSingleton<ITemporalWorkerContribution, GroceryRunWorkerContribution>();
        }
        else
        {
            services.AddSingleton<IGroceryRunCoordinator, UnavailableGroceryRunCoordinator>();
        }

        return services;
    }
}
