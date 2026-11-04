using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.Auth.Contracts;

/// <summary>
/// The body of <c>POST /api/auth/login</c>.
/// </summary>
/// <remarks>
/// A class rather than a record so the password can never appear in a synthesized <c>ToString()</c> and leak
/// into logs. Validated automatically by the minimal API validation filter (<c>AddValidation</c>).
/// </remarks>
public sealed class LoginRequest
{
    /// <summary>Gets the email address the account was registered with.</summary>
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email addresses can be at most {1} characters.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the account password.</summary>
    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(PasswordPolicy.MaximumLength, ErrorMessage = "Passwords can be at most {1} characters.")]
    public string Password { get; init; } = string.Empty;
}
