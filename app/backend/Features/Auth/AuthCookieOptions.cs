using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Settings for the cookie that carries the access token, bound from the <c>AuthCookie</c> configuration section.
/// </summary>
internal sealed class AuthCookieOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "AuthCookie";

    private const string HostPrefix = "__Host-";

    /// <summary>
    /// Gets or sets the cookie name. The <c>__Host-</c> prefix makes browsers insist that the cookie is Secure,
    /// scoped to path <c>/</c>, and bound to this exact host (no <c>Domain</c> attribute), which stops sibling
    /// subdomains from overwriting it.
    /// </summary>
    [Required]
    public string Name { get; set; } = "__Host-LoginApp.Auth";

    /// <summary>
    /// Gets or sets a value indicating whether the cookie is only sent over HTTPS. Turned off for local
    /// development only, where the site is served over plain <c>http://localhost</c>.
    /// </summary>
    public bool Secure { get; set; } = true;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Browsers silently drop a __Host- cookie that is not Secure, which would surface as "login does nothing".
        if (Name.StartsWith(HostPrefix, StringComparison.Ordinal) && !Secure)
        {
            yield return new ValidationResult(
                $"AuthCookie:Name uses the '{HostPrefix}' prefix, which requires AuthCookie:Secure to be true.",
                [nameof(Name), nameof(Secure)]);
        }
    }
}
