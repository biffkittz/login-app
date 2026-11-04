using System.ComponentModel.DataAnnotations;
using System.Net;

namespace LoginApp.Api.Infrastructure.Networking;

/// <summary>
/// Which networks count as internal, bound from the <c>Network</c> configuration section.
/// </summary>
/// <remarks>
/// The same list serves two purposes: it is the allow-list for <see cref="InternalNetworkRestrictionMiddleware"/>,
/// and it identifies the reverse proxies (Container Apps ingress, Application Gateway) whose
/// <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> headers are trusted.
/// </remarks>
internal sealed class NetworkOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Network";

    /// <summary>
    /// Gets or sets a value indicating whether connections from outside <see cref="InternalNetworks"/> are
    /// refused with 403.
    /// </summary>
    public bool RestrictToInternalNetworks { get; set; } = true;

    /// <summary>
    /// Gets or sets the internal address ranges in CIDR notation (e.g. <c>10.0.0.0/8</c>): the VNet address
    /// space, the Container Apps infrastructure subnet and loopback. The defaults live in appsettings.json.
    /// </summary>
    /// <remarks>
    /// Overriding one entry with an environment variable (<c>Network__InternalNetworks__0</c>) replaces only that
    /// index; the remaining entries from appsettings.json still apply.
    /// </remarks>
    public string[] InternalNetworks { get; set; } = [];

    /// <summary>
    /// Parses <see cref="InternalNetworks"/>.
    /// </summary>
    /// <returns>The parsed networks.</returns>
    /// <exception cref="FormatException">An entry is not valid CIDR notation. Options validation rejects such
    /// values at startup, so this indicates the options were never validated.</exception>
    public IReadOnlyList<IPNetwork> ParseInternalNetworks() =>
        Array.ConvertAll(InternalNetworks, static cidr => IPNetwork.Parse(cidr.Trim()));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RestrictToInternalNetworks && InternalNetworks.Length == 0)
        {
            yield return new ValidationResult(
                "Network:InternalNetworks must list at least one CIDR range while Network:RestrictToInternalNetworks is true.",
                [nameof(InternalNetworks)]);
        }

        foreach (string cidr in InternalNetworks)
        {
            if (!IPNetwork.TryParse(cidr.Trim(), out _))
            {
                yield return new ValidationResult(
                    $"Network:InternalNetworks entry '{cidr}' is not a valid CIDR range (expected e.g. 10.0.0.0/8).",
                    [nameof(InternalNetworks)]);
            }
        }
    }
}
