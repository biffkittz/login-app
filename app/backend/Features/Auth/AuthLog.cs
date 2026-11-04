using Microsoft.AspNetCore.Identity;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Source-generated, allocation-free log messages for authentication events.
/// </summary>
/// <remarks>
/// Messages identify users by their opaque ID only. Email addresses are personal data and passwords and tokens
/// are secrets, so none of them are ever logged.
/// </remarks>
internal static partial class AuthLog
{
    /// <summary>
    /// Logs the Identity error codes (never the messages, which can echo user input) for a rejected registration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="errors">The errors Identity reported.</param>
    public static void RegistrationRejected(ILogger logger, IEnumerable<IdentityError> errors)
    {
        // Joining the codes allocates, so only do it when the message will actually be written.
        if (logger.IsEnabled(LogLevel.Information))
        {
            string errorCodes = string.Join(", ", errors.Select(error => error.Code));
            RegistrationRejected(logger, errorCodes);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "User {UserId} registered")]
    public static partial void UserRegistered(ILogger logger, string userId);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Registration rejected: {ErrorCodes}")]
    private static partial void RegistrationRejected(ILogger logger, string errorCodes);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "User {UserId} signed in")]
    public static partial void UserSignedIn(ILogger logger, string userId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Failed sign-in for user {UserId}")]
    public static partial void SignInFailed(ILogger logger, string userId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Failed sign-in for an unknown email address")]
    public static partial void SignInFailedUnknownUser(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "User {UserId} is locked out after repeated failed sign-ins")]
    public static partial void UserLockedOut(ILogger logger, string userId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning,
        Message = "No Jwt:SigningKey is configured, so a random key is being used for this process and sign-ins end when the API restarts. Persist one with: dotnet user-secrets set Jwt:SigningKey \"$(openssl rand -base64 48)\"")]
    public static partial void UsingEphemeralSigningKey(ILogger logger);
}
