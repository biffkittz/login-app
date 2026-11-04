using LoginApp.Api.Features.Auth.Contracts;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// The outcome of a successful sign-in: who signed in and the token proving it.
/// </summary>
/// <remarks>A class rather than a record so the token cannot leak through a synthesized <c>ToString()</c>.</remarks>
/// <param name="user">The signed-in user, ready to return to the client.</param>
/// <param name="token">The access token to place in the auth cookie.</param>
internal sealed class AuthenticatedSession(UserResponse user, AccessToken token)
{
    /// <summary>Gets the signed-in user.</summary>
    public UserResponse User { get; } = user;

    /// <summary>Gets the access token that must be stored in the auth cookie.</summary>
    public AccessToken Token { get; } = token;
}
