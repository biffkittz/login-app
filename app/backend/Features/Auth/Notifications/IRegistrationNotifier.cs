namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Tells the people in <see cref="RegistrationNotificationOptions.Recipients"/> that someone has registered.
/// </summary>
internal interface IRegistrationNotifier
{
    /// <summary>
    /// Queues the email about a new account, when registration emails are on. Returns at once and never throws: the
    /// email goes out in the background, so registering neither waits for it nor fails because of it.
    /// </summary>
    /// <param name="displayName">The name the person registered with.</param>
    public void NotifyRegistered(string displayName);
}
