using LoginApp.Api.Infrastructure.Data;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Issues access tokens for authenticated users.
/// </summary>
internal interface IAccessTokenIssuer
{
    /// <summary>
    /// Creates a signed access token identifying <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The user who has just proven their identity.</param>
    /// <returns>The token and its expiry.</returns>
    public AccessToken Issue(ApplicationUser user);
}
