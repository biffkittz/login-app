using System.ComponentModel.DataAnnotations;
using System.Net;

namespace LoginApp.Api.Infrastructure.Temporal;

/// <summary>
/// How the app reaches Temporal, which runs its durable workflows (grocery runs), bound from the <c>Temporal</c>
/// configuration section.
/// </summary>
/// <remarks>
/// <para>
/// In Azure, Terraform points <see cref="Address"/> at a Temporal Cloud namespace and delivers <see cref="ApiKey"/>
/// from Key Vault. On a developer's machine, <c>temporal server start-dev</c> listens on <c>localhost:7233</c> without
/// TLS or a key. Left empty, there is no Temporal: the list works as always, and only grocery runs are unavailable.
/// </para>
/// <para>
/// The app's worker runs in this process and polls <see cref="TaskQueue"/>. The two sites share one namespace, so each
/// must have a task queue of its own: a site's workflows have to run where that site's database and phones are.
/// </para>
/// </remarks>
internal sealed class TemporalOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Temporal";

    /// <summary>The longest <see cref="TaskQueue"/>.</summary>
    public const int TaskQueueMaxLength = 100;

    /// <summary>
    /// Gets or sets the frontend's gRPC address as <c>host:port</c>: Temporal Cloud's namespace endpoint
    /// (<c>&lt;namespace&gt;.&lt;account&gt;.tmprl.cloud:7233</c>), or <c>localhost:7233</c> for the development
    /// server. <see langword="null"/> means there is no Temporal.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the namespace, such as <c>nibbles-demo.a1b2c</c> on Temporal Cloud or <c>default</c> on the
    /// development server.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Gets or sets the Temporal Cloud API key (a namespace-scoped service account's, with Write permission). A
    /// secret: Key Vault (<c>Temporal--ApiKey</c>) in Azure. With a key the connection always uses TLS.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the task queue this site's worker polls and its workflows run on. Unique per site (Terraform sets
    /// <c>nibbles-&lt;instance&gt;</c>).
    /// </summary>
    [Required(ErrorMessage = "Temporal:TaskQueue is required.")]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$",
        ErrorMessage = "Temporal:TaskQueue may hold letters, digits, '.', '_' and '-' only, up to 100 characters.")]
    public string TaskQueue { get; set; } = "nibbles";

    /// <summary>Gets a value indicating whether Temporal is configured, and with it grocery runs.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Address);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsConfigured)
        {
            // Half a configuration would silently leave grocery runs off; say so instead.
            if (!string.IsNullOrEmpty(Namespace) || !string.IsNullOrEmpty(ApiKey))
            {
                yield return new ValidationResult(
                    "Temporal:Namespace and Temporal:ApiKey need Temporal:Address as well.",
                    [nameof(Address)]);
            }

            yield break;
        }

        if (!TrySplitAddress(Address!, out string host))
        {
            yield return new ValidationResult(
                "Temporal:Address must be host:port, such as my-namespace.a1b2c.tmprl.cloud:7233 or localhost:7233.",
                [nameof(Address)]);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(Namespace) || Namespace.Any(char.IsWhiteSpace))
        {
            yield return new ValidationResult("Temporal:Namespace is required with Temporal:Address.", [nameof(Namespace)]);
        }

        // Without a key the connection is plain text, which is fine for the development server on this machine only.
        // Anywhere else it would send workflow data, and any credentials, across the network unencrypted.
        if (string.IsNullOrEmpty(ApiKey))
        {
            if (!IsLoopback(host))
            {
                yield return new ValidationResult(
                    "Temporal:ApiKey is required unless Temporal:Address is on this machine (the development server).",
                    [nameof(ApiKey)]);
            }
        }
        else if (ApiKey.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            yield return new ValidationResult(
                "Temporal:ApiKey must not contain spaces or control characters; check how it was copied.",
                [nameof(ApiKey)]);
        }
    }

    private static bool TrySplitAddress(string address, out string host)
    {
        host = string.Empty;
        int colon = address.LastIndexOf(':');
        if (colon <= 0
            || !int.TryParse(address.AsSpan(colon + 1), out int port)
            || port is < 1 or > 65535
            || address.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        host = address[..colon].Trim('[', ']');
        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
