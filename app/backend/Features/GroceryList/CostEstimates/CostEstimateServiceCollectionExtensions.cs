using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// Registers cost estimates for the grocery list: the Gemini client, the service and the daily limit.
/// </summary>
internal static class CostEstimateServiceCollectionExtensions
{
    // A long list's answer is a few dozen kilobytes of JSON. A much larger one isn't the answer asked for.
    private const long MaxResponseBytes = 2 * 1024 * 1024;

    // Flash-Lite prices a family's list in a few seconds. A minute allows for a long list on a busy day, and stays
    // under Cloudflare's 100-second limit on a proxied request, so the user gets the app's message rather than
    // Cloudflare's error page.
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Adds everything the Estimate Cost button needs.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGroceryCostEstimates(this IServiceCollection services)
    {
        services.AddOptions<CostEstimateOptions>()
            .BindConfiguration(CostEstimateOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ICostEstimator, GeminiCostEstimator>(static (serviceProvider, client) =>
        {
            CostEstimateOptions options = serviceProvider.GetRequiredService<IOptions<CostEstimateOptions>>().Value;

            // Without a trailing slash, the relative request path would replace the base address's last segment.
            string endpoint = options.Endpoint.AbsoluteUri;
            client.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : $"{endpoint}/");
            client.Timeout = s_requestTimeout;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CostEstimateBudget>();
        services.AddSingleton<CostEstimateMetrics>();
        services.AddScoped<CostEstimateService>();
        return services;
    }
}
