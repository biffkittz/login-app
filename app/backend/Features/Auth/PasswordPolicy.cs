namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The password rules enforced by ASP.NET Core Identity and request validation. The Vue frontend mirrors them in
/// <c>frontend/src/validation/account-policy.ts</c> so users get instant feedback; keep the two in sync.
/// </summary>
/// <remarks>
/// The policy is length-based, following NIST SP 800-63B: long passphrases resist guessing far better than
/// composition rules ("one upper, one digit, one symbol"), which push people toward predictable patterns such as
/// <c>Password1!</c>.
/// </remarks>
internal static class PasswordPolicy
{
    /// <summary>The minimum number of characters a password must contain.</summary>
    public const int MinimumLength = 12;

    /// <summary>The maximum accepted length; bounds the cost of hashing attacker-supplied input.</summary>
    public const int MaximumLength = 128;

    /// <summary>The number of distinct characters required, which rejects inputs such as <c>aaaaaaaaaaaa</c>.</summary>
    public const int RequiredUniqueChars = 4;
}
