using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Registers AI pictures for grocery items: the Gemini client, the background worker and its daily limit.
/// </summary>
internal static class GroceryImageServiceCollectionExtensions
{
    // A 1024-pixel PNG is one to two megabytes, a third more as Base64. A much larger answer isn't the picture we
    // asked for, and buffering it would only waste memory.
    private const long MaxResponseBytes = 16 * 1024 * 1024;

    // Gemini usually draws a picture in five to fifteen seconds. A minute allows for a busy moment without leaving an
    // item waiting for good.
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Adds everything that draws, stores and limits item pictures.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGroceryImages(this IServiceCollection services)
    {
        services.AddOptions<ImageGenerationOptions>()
            .BindConfiguration(ImageGenerationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IGroceryImageGenerator, GeminiImageGenerator>(static (serviceProvider, client) =>
        {
            ImageGenerationOptions options = serviceProvider.GetRequiredService<IOptions<ImageGenerationOptions>>().Value;

            // Without a trailing slash, the relative request path would replace the base address's last segment
            // (".../v1" + "models/..." is ".../models/...").
            string endpoint = options.Endpoint.AbsoluteUri;
            client.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : $"{endpoint}/");
            client.Timeout = s_requestTimeout;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<GroceryImageSignal>();
        services.AddSingleton<GroceryImageBudget>();
        services.AddSingleton<GroceryImageMetrics>();
        services.AddHostedService<GroceryImageWorker>();
        return services;
    }
}
