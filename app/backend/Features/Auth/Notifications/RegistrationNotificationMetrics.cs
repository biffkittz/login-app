using System.Diagnostics.Metrics;
using LoginApp.Api.Infrastructure.Telemetry;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// What became of the registration emails (<c>nibbles.registration_emails</c>, by outcome). Like the log, the metric
/// carries no address, name or text.
/// </summary>
internal sealed class RegistrationNotificationMetrics
{
    /// <summary>The attribute naming the outcome.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>Cloudflare accepted the email.</summary>
    public const string Sent = "sent";

    /// <summary>Cloudflare refused it, or every attempt failed.</summary>
    public const string Failed = "failed";

    /// <summary>The day's allowance was used up, so it wasn't sent.</summary>
    public const string DailyLimit = "daily_limit";

    /// <summary>The app stopped before it could be sent.</summary>
    public const string LostAtShutdown = "lost_at_shutdown";

    private readonly Counter<long> _emails;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistrationNotificationMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">Creates the app's meter.</param>
    public RegistrationNotificationMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(ApplicationMeter.Name);
        _emails = meter.CreateCounter<long>(
            "nibbles.registration_emails",
            "{email}",
            "Registration emails, by outcome.");
    }

    /// <summary>Records what became of one or more emails.</summary>
    /// <param name="outcome"><see cref="Sent"/>, <see cref="Failed"/>, <see cref="DailyLimit"/> or <see cref="LostAtShutdown"/>.</param>
    /// <param name="count">How many emails.</param>
    public void Emails(string outcome, int count = 1) => _emails.Add(count, new KeyValuePair<string, object?>(OutcomeTag, outcome));
}
