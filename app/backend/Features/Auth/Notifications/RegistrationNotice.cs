namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// A registration to email about. Only what the email says is kept: not the person's email address.
/// </summary>
/// <param name="DisplayName">The name they registered with, as they typed it.</param>
/// <param name="RegisteredAt">When the account was created.</param>
internal sealed record RegistrationNotice(string DisplayName, DateTimeOffset RegisteredAt);
