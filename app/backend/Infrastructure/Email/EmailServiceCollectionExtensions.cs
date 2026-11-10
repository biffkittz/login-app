using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// Registers sending email through Cloudflare Email Service.
/// </summary>
internal static class EmailServiceCollectionExtensions
{
    // Cloudflare's answer is a short JSON document listing the recipients. Anything much bigger isn't that answer.
    private const long MaxResponseBytes = 256 * 1024;

    // Cloudflare usually answers within a second or two. Waiting longer only delays the next email in the queue.
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Adds <see cref="IEmailSender"/> and its options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IEmailSender, CloudflareEmailSender>(static (serviceProvider, client) =>
        {
            EmailOptions options = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;

            // Without a trailing slash, the relative request path would replace the base address's last segment.
            string endpoint = options.Endpoint.AbsoluteUri;
            client.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : $"{endpoint}/");
            client.Timeout = s_requestTimeout;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
        });

        return services;
    }
}
