using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Per-user limits for changes to the grocery list and for opening realtime connections, bound from
/// <c>RateLimiting:GroceryList</c>. Every change is broadcast to every connected phone, so one runaway or hostile
/// client must not be able to flood everyone else.
/// </summary>
internal sealed class GroceryListRateLimitOptions
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "RateLimiting:GroceryList";

    /// <summary>Gets or sets how many requests one user may make per <see cref="Window"/>.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 120;

    /// <summary>Gets or sets the length of the fixed rate-limiting window.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00", ParseLimitsInInvariantCulture = true)]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
