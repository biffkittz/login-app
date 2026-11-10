using System.Globalization;
using System.Text;
using LoginApp.Api.Infrastructure.Email;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Writes the email about a new registration.
/// </summary>
/// <remarks>
/// <para>
/// It says where and when someone registered, and the name they gave, which is usually enough to tell family from a
/// stranger. Their email address stays out: it is personal data, and the app copies it nowhere, logs included.
/// </para>
/// <para>
/// The name is the registrant's own text, so it is quoted, labelled as theirs, and stripped of characters that could
/// disguise it: line breaks, which would let it pose as further lines of the email, and the invisible controls that
/// make text read in a different order than it was written.
/// </para>
/// </remarks>
internal static class RegistrationEmail
{
    // Stands in for each character taken out of a name, so the reader can see that something was there.
    private const char Replacement = '\uFFFD';

    /// <summary>
    /// Writes the email about <paramref name="notice"/> to the configured recipients.
    /// </summary>
    /// <param name="notice">The registration.</param>
    /// <param name="options">The recipients, subject and site.</param>
    /// <param name="isLastForToday">This is the last email the daily limit allows, which the email then says.</param>
    /// <returns>The email.</returns>
    public static EmailMessage Create(RegistrationNotice notice, RegistrationNotificationOptions options, bool isLastForToday)
    {
        string site = options.SiteUrl is Uri url ? url.GetLeftPart(UriPartial.Path).TrimEnd('/') : "Nibbles";
        StringBuilder text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Someone has just registered a new account on {site}.\n\n")
            .Append(CultureInfo.InvariantCulture, $"Name, as they typed it: \"{Sanitize(notice.DisplayName)}\"\n")
            .Append(CultureInfo.InvariantCulture, $"Registered at: {notice.RegisteredAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC\n");

        if (isLastForToday)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nThat makes {options.MaxPerDay} registration emails in 24 hours, ")
                .Append("the most RegistrationNotifications:MaxPerDay allows. Until the count drops, further registrations ")
                .Append("aren't emailed; the app's log still records them.\n");
        }

        text.Append("\nYou get this email because your address is in the app's RegistrationNotifications:Recipients ")
            .Append("setting (registration_notification_recipients in its Terraform).\n");

        return new EmailMessage([.. options.Recipients], options.Subject, text.ToString());
    }

    /// <summary>
    /// Replaces the characters of <paramref name="displayName"/> that could disguise it with U+FFFD.
    /// </summary>
    /// <param name="displayName">The name as the person typed it.</param>
    /// <returns>The name, safe to show in a line of the email.</returns>
    internal static string Sanitize(string displayName) =>
        string.Create(displayName.Length, displayName, static (span, name) =>
        {
            for (int i = 0; i < name.Length; i++)
            {
                span[i] = IsDisguising(name[i]) ? Replacement : name[i];
            }
        });

    // Control characters (line breaks, tabs, escape), the line and paragraph separators, and the bidirectional
    // controls: marks (U+200E, U+200F, U+061C), embeddings and overrides (U+202A-U+202E) and isolates (U+2066-U+2069).
    // Zero-width joiners stay, because emoji sequences such as a family need them.
    private static bool IsDisguising(char character) =>
        char.IsControl(character)
        || character is '\u2028' or '\u2029' or '\u200E' or '\u200F' or '\u061C'
        || character is >= '\u202A' and <= '\u202E'
        || character is >= '\u2066' and <= '\u2069';
}
