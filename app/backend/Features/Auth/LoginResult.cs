using System.Diagnostics.CodeAnalysis;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The result of <see cref="IAuthService.LoginAsync"/>. Expected failures (wrong password, lockout) are values
/// rather than exceptions, so the endpoint can map each one to an HTTP response explicitly.
/// </summary>
internal sealed class LoginResult
{
    private LoginResult(LoginStatus status, AuthenticatedSession? session)
    {
        Status = status;
        Session = session;
    }

    /// <summary>Gets the result for an unknown email address or wrong password.</summary>
    public static LoginResult InvalidCredentials { get; } = new(LoginStatus.InvalidCredentials, session: null);

    /// <summary>Gets the result for an account that is temporarily locked out.</summary>
    public static LoginResult LockedOut { get; } = new(LoginStatus.LockedOut, session: null);

    /// <summary>Gets the outcome of the attempt.</summary>
    public LoginStatus Status { get; }

    /// <summary>Gets the new session; non-null exactly when <see cref="Succeeded"/> is true.</summary>
    public AuthenticatedSession? Session { get; }

    /// <summary>Gets a value indicating whether the user is now signed in.</summary>
    [MemberNotNullWhen(true, nameof(Session))]
    public bool Succeeded => Status == LoginStatus.Succeeded;

    /// <summary>Creates a successful result.</summary>
    /// <param name="session">The session that was created.</param>
    /// <returns>A result whose <see cref="Session"/> is <paramref name="session"/>.</returns>
    public static LoginResult Success(AuthenticatedSession session) => new(LoginStatus.Succeeded, session);
}
