using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Settings for issuing and validating JSON Web Tokens, bound from the <c>Jwt</c> configuration section.
/// </summary>
internal sealed class JwtOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// The minimum signing key size. HMAC-SHA256 keys must be at least 256 bits (RFC 7518, section 3.2).
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Gets or sets the <c>iss</c> claim written to, and required on, every token.</summary>
    [Required]
    public string Issuer { get; set; } = "login-app";

    /// <summary>Gets or sets the <c>aud</c> claim written to, and required on, every token.</summary>
    [Required]
    public string Audience { get; set; } = "login-app";

    /// <summary>
    /// Gets or sets how long a token (and the cookie carrying it) stays valid. Tokens cannot be revoked once
    /// issued, so this is deliberately short.
    /// </summary>
    [Range(1, 24 * 60)]
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets the Base64-encoded HMAC-SHA256 signing key. Generate one with <c>openssl rand -base64 48</c>.
    /// In Azure it comes from Key Vault (secret <c>Jwt--SigningKey</c>); it must never be committed.
    /// </summary>
    [Required(ErrorMessage = "Jwt:SigningKey is not configured. Generate one with 'openssl rand -base64 48' and supply it through user-secrets (development) or Azure Key Vault (secret 'Jwt--SigningKey').")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Creates the symmetric key used to sign and validate tokens.
    /// </summary>
    /// <returns>A key wrapping the decoded <see cref="SigningKey"/> bytes.</returns>
    /// <exception cref="FormatException"><see cref="SigningKey"/> is not valid Base64. Options validation
    /// rejects such values at startup, so this indicates the options were never validated.</exception>
    public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Base64 always decodes to fewer bytes than it has characters, so this buffer is always large enough.
        byte[] buffer = new byte[SigningKey.Length];
        if (!Convert.TryFromBase64String(SigningKey, buffer, out int keyLength) || keyLength < MinimumSigningKeyBytes)
        {
            yield return new ValidationResult(
                $"Jwt:SigningKey must be Base64 encoding at least {MinimumSigningKeyBytes} bytes (256 bits).",
                [nameof(SigningKey)]);
        }
    }
}
