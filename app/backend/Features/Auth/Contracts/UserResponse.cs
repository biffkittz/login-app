namespace LoginApp.Api.Features.Auth.Contracts;

/// <summary>
/// The signed-in user, as returned by the register, login and "me" endpoints.
/// </summary>
/// <param name="Id">The user's stable identifier.</param>
/// <param name="Email">The email address the user signs in with.</param>
/// <param name="DisplayName">The name to greet the user by.</param>
/// <param name="SessionExpiresAt">When the current sign-in (the access token in the auth cookie) expires. The SPA
/// uses it to return the user to the login page at exactly the moment the cookie stops working.</param>
internal sealed record UserResponse(string Id, string Email, string DisplayName, DateTimeOffset SessionExpiresAt);
