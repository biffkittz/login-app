using System.Security.Claims;
using LoginApp.Api.Features.Auth.Contracts;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Account registration and sign-in, independent of HTTP concerns such as cookies and status codes.
/// </summary>
internal interface IAuthService
{
    /// <summary>
    /// Creates an account and signs the new user in.
    /// </summary>
    /// <param name="request">The validated registration details.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new session, or field-level errors explaining why the account was not created.</returns>
    public Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies credentials, applying Identity's lockout policy on failure.
    /// </summary>
    /// <param name="request">The validated login details.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new session, or why sign-in was refused.</returns>
    public Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the user identified by an authenticated principal.
    /// </summary>
    /// <param name="principal">The principal produced by the JWT bearer handler.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user, or <see langword="null"/> if the token is malformed or the account no longer exists.</returns>
    public Task<UserResponse?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
