namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// Sends email from the app's own address.
/// </summary>
internal interface IEmailSender
{
    /// <summary>
    /// Hands an email to the mail service.
    /// </summary>
    /// <param name="message">The email.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the service did with it.</returns>
    /// <exception cref="EmailDeliveryException">The service didn't take the email.</exception>
    /// <exception cref="InvalidOperationException">Email isn't configured (<see cref="EmailOptions.IsConfigured"/>).</exception>
    public Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
