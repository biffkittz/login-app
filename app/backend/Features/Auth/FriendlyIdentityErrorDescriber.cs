using Microsoft.AspNetCore.Identity;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Rewords the Identity validation messages that users of this app can actually trigger.
/// </summary>
/// <remarks>
/// Identity speaks of "user names", but here the user name is the email address, so the stock messages
/// ("Username 'x' is invalid, can only contain letters or digits") would confuse people. The error
/// <see cref="IdentityError.Code"/> values are unchanged, which is what <see cref="AuthService"/> uses to decide
/// which form field each message belongs to.
/// </remarks>
internal sealed class FriendlyIdentityErrorDescriber : IdentityErrorDescriber
{
    private const string EmailTakenMessage = "An account with this email address already exists.";
    private const string InvalidEmailMessage = "Enter a valid email address.";

    /// <inheritdoc />
    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = EmailTakenMessage };

    /// <inheritdoc />
    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = EmailTakenMessage };

    /// <inheritdoc />
    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = InvalidEmailMessage };

    /// <inheritdoc />
    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = InvalidEmailMessage };

    /// <inheritdoc />
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Use at least {length} characters." };

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new()
        {
            Code = nameof(PasswordRequiresUniqueChars),
            Description = $"Use at least {uniqueChars} different characters.",
        };
}
