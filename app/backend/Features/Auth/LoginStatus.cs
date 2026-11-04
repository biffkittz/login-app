namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The possible outcomes of a login attempt.
/// </summary>
internal enum LoginStatus
{
    /// <summary>The credentials were valid and a session was created.</summary>
    Succeeded,

    /// <summary>The email address is unknown or the password is wrong (deliberately indistinguishable).</summary>
    InvalidCredentials,

    /// <summary>Too many recent failures; the account is temporarily locked.</summary>
    LockedOut,
}
