using System.Diagnostics.CodeAnalysis;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The result of <see cref="IAuthService.RegisterAsync"/>: either a new session or the reasons the account could
/// not be created, keyed by request field so the UI can show each message next to its input.
/// </summary>
internal sealed class RegistrationResult
{
    private RegistrationResult(AuthenticatedSession? session, IReadOnlyDictionary<string, string[]> errors)
    {
        Session = session;
        Errors = errors;
    }

    /// <summary>Gets the new session; non-null exactly when <see cref="Succeeded"/> is true.</summary>
    public AuthenticatedSession? Session { get; }

    /// <summary>
    /// Gets validation messages keyed by request property name (<c>Email</c>, <c>Password</c>), matching the keys
    /// of the automatic request validation. An empty key holds errors that do not belong to a single field.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Gets a value indicating whether the account was created and the user signed in.</summary>
    [MemberNotNullWhen(true, nameof(Session))]
    public bool Succeeded => Session is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="session">The session for the newly registered user.</param>
    /// <returns>A successful result.</returns>
    public static RegistrationResult Success(AuthenticatedSession session) =>
        new(session, new Dictionary<string, string[]>(capacity: 0));

    /// <summary>Creates a failed result.</summary>
    /// <param name="errors">Messages keyed by request field.</param>
    /// <returns>A failed result.</returns>
    public static RegistrationResult Failure(IReadOnlyDictionary<string, string[]> errors) => new(session: null, errors);
}
