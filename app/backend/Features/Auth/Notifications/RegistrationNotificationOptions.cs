using System.ComponentModel.DataAnnotations;
using LoginApp.Api.Infrastructure.Email;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Who hears about new registrations, bound from the <c>RegistrationNotifications</c> configuration section.
/// </summary>
/// <remarks>
/// Every registration on this instance of the app sends one email to <see cref="Recipients"/>, once email itself is
/// configured (<see cref="EmailOptions"/>). Terraform sets both in Azure; locally the list is empty, so nothing is sent.
/// </remarks>
internal sealed class RegistrationNotificationOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "RegistrationNotifications";

    /// <summary>The longest <see cref="Subject"/>.</summary>
    public const int SubjectMaxLength = 200;

    /// <summary>
    /// Gets or sets who gets an email when someone registers. Empty, nobody does and registration emails are off.
    /// </summary>
    /// <remarks>
    /// Not defaulted here: the configuration binder adds configured entries to a list's existing items instead of
    /// replacing them.
    /// </remarks>
    public List<string> Recipients { get; set; } = [];

    /// <summary>Gets or sets the subject line of every registration email.</summary>
    [Required(ErrorMessage = "RegistrationNotifications:Subject must not be empty.")]
    [StringLength(SubjectMaxLength, ErrorMessage = "RegistrationNotifications:Subject can be at most {1} characters.")]
    public string Subject { get; set; } = "[grocery app] new registration detected";

    /// <summary>
    /// Gets or sets this instance's public address, such as <c>https://grocery.biffkittz.com</c>. Several instances email
    /// the same people, so the email says which site was registered on. Optional.
    /// </summary>
    public Uri? SiteUrl { get; set; }

    /// <summary>
    /// Gets or sets how many registration emails may be sent in any 24 hours. Anyone can register, so this caps how hard a
    /// flood of sign-ups (a script, say) can fill the recipients' inboxes; the last email allowed says so. Registrations
    /// over the limit still succeed and are logged. 0 sends none.
    /// </summary>
    [Range(0, 1000, ErrorMessage = "RegistrationNotifications:MaxPerDay must be between {1} and {2}.")]
    public int MaxPerDay { get; set; } = 50;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Recipients.TrueForAll(EmailAddresses.IsPlain))
        {
            yield return new ValidationResult(
                "RegistrationNotifications:Recipients must be plain email addresses, such as someone@example.com.",
                [nameof(Recipients)]);
        }

        if (Recipients.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Recipients.Count)
        {
            yield return new ValidationResult("RegistrationNotifications:Recipients must not repeat an address.", [nameof(Recipients)]);
        }

        if (Recipients.Count > CloudflareEmailSender.MaxRecipients)
        {
            yield return new ValidationResult(
                $"RegistrationNotifications:Recipients can list at most {CloudflareEmailSender.MaxRecipients} addresses, the most one email can have.",
                [nameof(Recipients)]);
        }

        // The subject becomes a header, where a line break could start a header of its own.
        if (Subject.Any(char.IsControl))
        {
            yield return new ValidationResult("RegistrationNotifications:Subject must not contain control characters.", [nameof(Subject)]);
        }

        if (SiteUrl is not null
            && (!SiteUrl.IsAbsoluteUri || (SiteUrl.Scheme != Uri.UriSchemeHttps && SiteUrl.Scheme != Uri.UriSchemeHttp)))
        {
            yield return new ValidationResult(
                "RegistrationNotifications:SiteUrl must be an absolute http(s):// URL, such as https://app.example.com.",
                [nameof(SiteUrl)]);
        }
    }
}
