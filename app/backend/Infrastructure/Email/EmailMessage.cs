namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// A plain-text email from the configured sender (<see cref="EmailOptions.SenderAddress"/>).
/// </summary>
/// <remarks>
/// Plain text only: nothing a user typed can turn into markup, links styled as buttons or remote images in someone's
/// inbox.
/// </remarks>
/// <param name="To">Who receives it: 1 to <see cref="CloudflareEmailSender.MaxRecipients"/> addresses.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Text">The body.</param>
internal sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Text);
