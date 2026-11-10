using LoginApp.Api.Features.GroceryList.CostEstimates;
using LoginApp.Api.Features.GroceryList.Images;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Registers the grocery list feature: its options, service, realtime hub, rate limit, item pictures and cost
/// estimates.
/// </summary>
internal static class GroceryListServiceCollectionExtensions
{
    // Clients send SignalR only its handshake and keep-alive pings, a few dozen bytes each: the hub has no methods to
    // call. A small cap (the default is 32 KB) bounds what any one connection can make the server buffer.
    private const long MaximumReceiveMessageBytes = 4 * 1024;

    /// <summary>
    /// Adds everything the <c>/api/grocery-list</c> endpoints and hub need.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGroceryListFeature(this IServiceCollection services)
    {
        services.AddOptions<GroceryListOptions>()
            .BindConfiguration(GroceryListOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // SignalR runs inside the app (not Azure SignalR Service): the app is a single instance, so its own hub
        // reaches every connected client.
        services.AddSignalR(static hub => hub.MaximumReceiveMessageSize = MaximumReceiveMessageBytes);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<GroceryListWriteLock>();
        services.AddSingleton<IGroceryListNotifier, GroceryListHubNotifier>();
        services.AddScoped<IGroceryListService, GroceryListService>();
        services.AddGroceryListRateLimiting();
        services.AddGroceryImages();
        services.AddGroceryCostEstimates();

        return services;
    }
}
