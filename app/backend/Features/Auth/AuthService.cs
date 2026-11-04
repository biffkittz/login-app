using System.Globalization;
using System.Security.Claims;
using LoginApp.Api.Features.Auth.Contracts;
using LoginApp.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Registration and sign-in built on ASP.NET Core Identity, which handles password hashing (PBKDF2), uniqueness
/// checks and account lockout.
/// </summary>
/// <remarks>
/// <see cref="UserManager{TUser}"/> methods take no <see cref="CancellationToken"/>. It is registered as
/// <see cref="AspNetUserManager{TUser}"/>, which passes <c>HttpContext.RequestAborted</c> to every store call, so
/// database work still stops when the client disconnects. The explicit check at the start of each method also
/// skips work the store can't cancel, such as the deliberately slow password hash, which runs before any store
/// call. Nothing checks for cancellation after a change has been saved: by then cancelling can't undo the change,
/// and would only skip logging it.
/// </remarks>
/// <param name="userManager">Identity's user API.</param>
/// <param name="signInManager">Identity's sign-in API, used for password checks with lockout.</param>
/// <param name="tokenIssuer">Issues the access token for a successful sign-in.</param>
/// <param name="logger">The logger.</param>
internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAccessTokenIssuer tokenIssuer,
    ILogger<AuthService> logger) : IAuthService
{
    // Error keys match the request property names, the same keys the validation filter uses for its errors.
    private const string EmailField = nameof(RegisterRequest.Email);
    private const string PasswordField = nameof(RegisterRequest.Password);
    private const string GeneralField = "";

    /// <inheritdoc />
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string email = request.Email.Trim();
        ApplicationUser user = new()
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
        };

        IdentityResult result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            AuthLog.RegistrationRejected(logger, result.Errors);
            return RegistrationResult.Failure(GroupErrorsByField(result.Errors));
        }

        // No cancellation check from here on: the account is saved, so the remaining work (logging it and
        // signing a token) must finish even if the client has gone.
        AuthLog.UserRegistered(logger, user.Id);
        return RegistrationResult.Success(CreateSession(user));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            AuthLog.SignInFailedUnknownUser(logger);
            return LoginResult.InvalidCredentials;
        }

        // Verifies the password *and* maintains Identity's lockout counter: failures count towards a temporary
        // lockout and a success resets the count. Unlike PasswordSignInAsync it does not issue Identity's own
        // cookie; the caller issues a JWT instead.
        // The attempt is now recorded (failed-sign-in count, possibly a lockout), so its outcome is always logged;
        // there is deliberately no cancellation check after this point.
        SignInResult signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (signIn.Succeeded)
        {
            AuthLog.UserSignedIn(logger, user.Id);
            return LoginResult.Success(CreateSession(user));
        }

        if (signIn.IsLockedOut)
        {
            AuthLog.UserLockedOut(logger, user.Id);
            return LoginResult.LockedOut;
        }

        AuthLog.SignInFailed(logger, user.Id);
        return LoginResult.InvalidCredentials;
    }

    /// <inheritdoc />
    public async Task<UserResponse?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The bearer handler keeps the registered claim names (MapInboundClaims = false), so the ID is in "sub".
        string? userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId) || !TryGetTokenExpiry(principal, out DateTimeOffset expiresAt))
        {
            return null;
        }

        // Re-read the account instead of trusting the token's claims, so a deleted account loses access at once.
        ApplicationUser? user = await userManager.FindByIdAsync(userId);
        return user is null ? null : ToResponse(user, expiresAt);
    }

    private AuthenticatedSession CreateSession(ApplicationUser user)
    {
        AccessToken token = tokenIssuer.Issue(user);
        return new AuthenticatedSession(ToResponse(user, token.ExpiresAt), token);
    }

    private static UserResponse ToResponse(ApplicationUser user, DateTimeOffset sessionExpiresAt) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, sessionExpiresAt);

    private static bool TryGetTokenExpiry(ClaimsPrincipal principal, out DateTimeOffset expiresAt)
    {
        // "exp" holds seconds since the Unix epoch (RFC 7519, section 4.1.4). Lifetime validation already
        // guarantees it exists; parsing defensively keeps a malformed token from becoming a 500.
        string? exp = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);
        if (long.TryParse(exp, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }

        expiresAt = default;
        return false;
    }

    // Duplicate-email registrations produce both DuplicateUserName and DuplicateEmail (the user name *is* the
    // email) with the same friendly text, so messages are de-duplicated per field.
    private static Dictionary<string, string[]> GroupErrorsByField(IEnumerable<IdentityError> errors) =>
        errors
            .GroupBy(error => FieldFor(error.Code), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

    // Identity error codes are the names of the IdentityErrorDescriber methods that produce them.
    private static string FieldFor(string errorCode) => errorCode switch
    {
        nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName)
            or nameof(IdentityErrorDescriber.InvalidEmail)
            or nameof(IdentityErrorDescriber.InvalidUserName) => EmailField,
        _ when errorCode.StartsWith("Password", StringComparison.Ordinal) => PasswordField,
        _ => GeneralField,
    };
}
