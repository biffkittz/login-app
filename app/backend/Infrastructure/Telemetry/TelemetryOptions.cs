using System.ComponentModel.DataAnnotations;
using System.Net;

namespace LoginApp.Api.Infrastructure.Telemetry;

/// <summary>
/// Where the app sends its metrics, bound from the <c>Telemetry</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// The app pushes its metrics to Prometheus in the OpenTelemetry protocol (OTLP over HTTP) every
/// <see cref="ExportIntervalSeconds"/>. Pushing needs no extra port and no public <c>/metrics</c> endpoint: the app's
/// only port faces Cloudflare, and Prometheus runs in the same Container Apps environment with internal ingress.
/// </para>
/// <para>
/// Export is off unless <see cref="OtlpEndpoint"/> is set. Terraform sets it in Azure when the observability stack
/// exists; on a developer's machine it is normally empty, and the instruments simply record into nothing.
/// </para>
/// </remarks>
internal sealed class TelemetryOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>The shortest allowed <see cref="ExportIntervalSeconds"/>.</summary>
    public const int MinExportIntervalSeconds = 1;

    /// <summary>The longest allowed <see cref="ExportIntervalSeconds"/>.</summary>
    public const int MaxExportIntervalSeconds = 300;

    /// <summary>The longest <see cref="Site"/>: a DNS name.</summary>
    public const int SiteMaxLength = 253;

    /// <summary>
    /// Gets or sets the full URL that receives OTLP metrics, such as Prometheus'
    /// <c>https://&lt;host&gt;/api/v1/otlp/v1/metrics</c>, or <see langword="null"/> to export nothing.
    /// </summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>Gets or sets the user name for HTTP Basic authentication at <see cref="OtlpEndpoint"/>.</summary>
    public string? OtlpUsername { get; set; }

    /// <summary>
    /// Gets or sets the password for <see cref="OtlpUsername"/>. A secret: Key Vault (<c>Telemetry--OtlpPassword</c>)
    /// in Azure, where Terraform generates it.
    /// </summary>
    public string? OtlpPassword { get; set; }

    /// <summary>Gets or sets how often metrics are sent, in seconds.</summary>
    [Range(MinExportIntervalSeconds, MaxExportIntervalSeconds, ErrorMessage = "Telemetry:ExportIntervalSeconds must be between {1} and {2}.")]
    public int ExportIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets the site this instance serves (its host name in Azure, such as <c>app.biffkittz.com</c>). It goes
    /// with every metric as the <c>site</c> resource attribute, which keeps the instances' series apart.
    /// </summary>
    [StringLength(SiteMaxLength, ErrorMessage = "Telemetry:Site can be at most {1} characters.")]
    public string? Site { get; set; }

    /// <summary>Gets a value indicating whether metrics are exported.</summary>
    public bool IsExporting => OtlpEndpoint is not null;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OtlpEndpoint is not null)
        {
            // Plain HTTP only reaches a Prometheus on this machine. Anything else would send the password across the
            // network unencrypted.
            bool isHttps = OtlpEndpoint.IsAbsoluteUri && OtlpEndpoint.Scheme == Uri.UriSchemeHttps;
            bool isLocalHttp = OtlpEndpoint.IsAbsoluteUri && OtlpEndpoint.Scheme == Uri.UriSchemeHttp && IsLoopback(OtlpEndpoint);
            if (!isHttps && !isLocalHttp)
            {
                yield return new ValidationResult(
                    "Telemetry:OtlpEndpoint must be an absolute https:// URL (http:// only for localhost).",
                    [nameof(OtlpEndpoint)]);
            }
        }

        // Half a set of credentials is a mistake (a missing secret, a typo in a variable name), and credentials without
        // an endpoint would go nowhere.
        bool hasUsername = !string.IsNullOrEmpty(OtlpUsername);
        bool hasPassword = !string.IsNullOrEmpty(OtlpPassword);
        if (hasUsername != hasPassword || ((hasUsername || hasPassword) && OtlpEndpoint is null))
        {
            yield return new ValidationResult(
                "Telemetry:OtlpUsername and Telemetry:OtlpPassword go together, and only with Telemetry:OtlpEndpoint.",
                [nameof(OtlpUsername), nameof(OtlpPassword)]);
        }

        // HTTP Basic authentication puts "user:password" in one header: a colon would split the user name, and a line
        // break would end the header.
        if (hasUsername && (OtlpUsername!.Contains(':', StringComparison.Ordinal) || OtlpUsername.Any(char.IsControl)))
        {
            yield return new ValidationResult(
                "Telemetry:OtlpUsername must not contain a colon or control characters.",
                [nameof(OtlpUsername)]);
        }

        if (hasPassword && OtlpPassword!.Any(char.IsControl))
        {
            yield return new ValidationResult("Telemetry:OtlpPassword must not contain control characters.", [nameof(OtlpPassword)]);
        }

        if (Site is not null && (Site.Length == 0 || Site.Any(char.IsControl)))
        {
            yield return new ValidationResult(
                "Telemetry:Site must be a name such as app.biffkittz.com, without control characters.",
                [nameof(Site)]);
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
