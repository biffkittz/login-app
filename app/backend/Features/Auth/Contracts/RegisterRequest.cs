using System.ComponentModel.DataAnnotations;
using LoginApp.Api.Infrastructure.Data;

namespace LoginApp.Api.Features.Auth.Contracts;

/// <summary>
/// The body of <c>POST /api/auth/register</c>.
/// </summary>
/// <remarks>
/// A class rather than a record so the password can never appear in a synthesized <c>ToString()</c> and leak
/// into logs. "Confirm password" is purely a client-side typo check, so it is not part of the API.
/// </remarks>
public sealed class RegisterRequest
{
    /// <summary>Gets the name to greet the user by.</summary>
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "Names can be at most {1} characters.")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the email address that will identify the account.</summary>
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email addresses can be at most {1} characters.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the new account's password; see <see cref="PasswordPolicy"/>.</summary>
    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(
        PasswordPolicy.MaximumLength,
        MinimumLength = PasswordPolicy.MinimumLength,
        ErrorMessage = "Use between {2} and {1} characters.")]
    public string Password { get; init; } = string.Empty;
}
