using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;

namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// How the app sends email through Cloudflare Email Service, bound from the <c>Email</c> configuration section.
/// </summary>
/// <remarks>
/// Sending is off unless the account, the token and the sender are all set. Terraform sets them in Azure when anyone is
/// to get registration emails; on a developer's machine they are normally empty, so nothing is ever sent from there.
/// </remarks>
internal sealed partial class EmailOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Email";

    /// <summary>The longest <see cref="SenderName"/>.</summary>
    public const int SenderNameMaxLength = 64;

    /// <summary>
    /// Gets or sets the base URL of Cloudflare's API. Emails are posted to <c>accounts/{AccountId}/email/sending/send</c>
    /// under it.
    /// </summary>
    [Required]
    public Uri Endpoint { get; set; } = new("https://api.cloudflare.com/client/v4/");

    /// <summary>Gets or sets the Cloudflare account that owns the sending domain: 32 hexadecimal characters.</summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// Gets or sets the Cloudflare API token the app sends with, which may do nothing but send email. A secret: Key Vault
    /// (<c>Email--ApiToken</c>) in Azure, where Terraform creates it; user secrets on a developer machine.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the address emails come from, in a domain onboarded for Cloudflare Email Sending, such as
    /// <c>notifications@biffkittz.com</c>.
    /// </summary>
    public string? SenderAddress { get; set; }

    /// <summary>Gets or sets the name shown next to <see cref="SenderAddress"/> in the recipient's inbox.</summary>
    [Required]
    [StringLength(SenderNameMaxLength, ErrorMessage = "Email:SenderName can be at most {1} characters.")]
    public string SenderName { get; set; } = "Nibbles";

    /// <summary>Gets a value indicating whether everything needed to send email is set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(SenderAddress);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Plain HTTP only reaches a stand-in for Cloudflare on this machine (tests). Anything else would send the token
        // across the network unencrypted.
        bool isHttps = Endpoint.IsAbsoluteUri && Endpoint.Scheme == Uri.UriSchemeHttps;
        bool isLocalHttp = Endpoint.IsAbsoluteUri && Endpoint.Scheme == Uri.UriSchemeHttp && IsLoopback(Endpoint);
        if (!isHttps && !isLocalHttp)
        {
            yield return new ValidationResult(
                "Email:Endpoint must be an absolute https:// URL (http:// only for localhost).",
                [nameof(Endpoint)]);
        }

        // Half a configuration is a mistake (a missing secret, a typo in a variable name), not a choice to send nothing.
        string?[] required = [AccountId, ApiToken, SenderAddress];
        if (!IsConfigured && required.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return new ValidationResult(
                "Email:AccountId, Email:ApiToken and Email:SenderAddress go together: set all three to send email, or none.",
                [nameof(AccountId), nameof(ApiToken), nameof(SenderAddress)]);
        }

        if (!string.IsNullOrEmpty(AccountId) && !AccountIdPattern().IsMatch(AccountId))
        {
            yield return new ValidationResult(
                "Email:AccountId must be a Cloudflare account ID: 32 lowercase hexadecimal characters.",
                [nameof(AccountId)]);
        }

        if (!string.IsNullOrEmpty(SenderAddress) && !EmailAddresses.IsPlain(SenderAddress))
        {
            yield return new ValidationResult(
                "Email:SenderAddress must be a plain email address, such as notifications@example.com.",
                [nameof(SenderAddress)]);
        }

        // The name becomes part of the From header, where a line break could start a header of its own.
        if (SenderName.Any(char.IsControl))
        {
            yield return new ValidationResult("Email:SenderName must not contain control characters.", [nameof(SenderName)]);
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();
}
