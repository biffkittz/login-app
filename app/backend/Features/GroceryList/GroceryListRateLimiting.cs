using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The rate-limiting policy applied to changes to the grocery list and to opening realtime connections.
/// </summary>
internal static class GroceryListRateLimiting
{
    /// <summary>The policy name referenced by <see cref="GroceryListEndpoints"/>.</summary>
    public const string PolicyName = "grocery-list";

    /// <summary>
    /// Registers the <see cref="PolicyName"/> policy with the rate limiter (set up by the auth feature).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGroceryListRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<GroceryListRateLimitOptions>()
            .BindConfiguration(GroceryListRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(static limiter => limiter.AddPolicy(PolicyName, CreatePartition));
        return services;
    }

    // One fixed window per user. Partitioning by user rather than IP address matters here: a family on one home
    // network shares an address, and must not share a budget. The rate limiter runs after authentication
    // (Program.cs), so the user is known; the address is only a fallback that authorization makes unreachable.
    private static RateLimitPartition<string> CreatePartition(HttpContext httpContext)
    {
        GroceryListRateLimitOptions options =
            httpContext.RequestServices.GetRequiredService<IOptions<GroceryListRateLimitOptions>>().Value;
        string? userId = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string partitionKey = userId is null
            ? $"address:{httpContext.Connection.RemoteIpAddress}"
            : $"user:{userId}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = options.Window,
            QueueLimit = 0,
        });
    }
}
