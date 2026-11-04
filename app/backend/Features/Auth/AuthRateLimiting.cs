using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The rate-limiting policy applied to the anonymous login and registration endpoints.
/// </summary>
internal static class AuthRateLimiting
{
    /// <summary>The policy name referenced by <see cref="AuthEndpoints"/>.</summary>
    public const string PolicyName = "authentication";

    /// <summary>
    /// Registers the rate limiter and the <see cref="PolicyName"/> policy.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .BindConfiguration(AuthRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(PolicyName, CreatePartition);
            limiter.OnRejected = static (context, _) => WriteRejectionAsync(context);
        });

        return services;
    }

    // One fixed window per client IP address. UseForwardedHeaders runs earlier in the pipeline, so behind
    // Application Gateway and the Container Apps ingress this is the real client address; without it every user
    // would share the proxy's single bucket and one noisy client would lock everybody out.
    private static RateLimitPartition<string> CreatePartition(HttpContext httpContext)
    {
        AuthRateLimitOptions options = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
        string clientAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = options.Window,
            QueueLimit = 0,
        });
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context)
    {
        HttpContext httpContext = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        IProblemDetailsService problemDetails = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many attempts. Please wait a moment and try again.",
            },
        });
    }
}
