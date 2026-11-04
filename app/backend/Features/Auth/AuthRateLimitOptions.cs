using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Per-client limits for the login and registration endpoints, bound from
/// <c>RateLimiting:Authentication</c>. They throttle credential stuffing and registration spam across
/// accounts; Identity's lockout separately protects each individual account.
/// </summary>
internal sealed class AuthRateLimitOptions
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "RateLimiting:Authentication";

    /// <summary>Gets or sets how many requests one client IP address may make per <see cref="Window"/>.</summary>
    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 10;

    /// <summary>Gets or sets the length of the fixed rate-limiting window.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00", ParseLimitsInInvariantCulture = true)]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
