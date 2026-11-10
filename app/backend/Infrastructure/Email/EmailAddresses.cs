using System.Net.Mail;

namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// Checks for the email addresses the app sends from and to, which come from configuration.
/// </summary>
internal static class EmailAddresses
{
    /// <summary>The longest address SMTP allows (RFC 5321).</summary>
    public const int MaxLength = 254;

    /// <summary>
    /// Determines whether <paramref name="value"/> is a bare address such as <c>name@example.com</c>: no display name, no
    /// angle brackets, no surrounding or embedded whitespace, and a domain with at least one dot.
    /// </summary>
    /// <param name="value">The text to check.</param>
    /// <returns><see langword="true"/> if it is a plain address.</returns>
    public static bool IsPlain(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
        && MailAddress.TryCreate(value, out MailAddress? address)
        && address.Address == value
        && address.DisplayName.Length == 0
        && address.Host.Contains('.', StringComparison.Ordinal);
}
