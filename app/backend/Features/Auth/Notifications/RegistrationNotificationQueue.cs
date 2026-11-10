using System.Threading.Channels;
using LoginApp.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Holds registration emails until <see cref="RegistrationNotificationWorker"/> sends them.
/// </summary>
/// <remarks>
/// The queue is in memory. An email still waiting when the app stops gets a few seconds to go out, then is lost (and
/// logged). Registrations are rare, so that trade is worth not adding a table for them.
/// </remarks>
/// <param name="notificationOptions">The recipients.</param>
/// <param name="emailOptions">Whether email can be sent at all.</param>
/// <param name="timeProvider">The clock, for the registration time.</param>
/// <param name="logger">The logger.</param>
internal sealed class RegistrationNotificationQueue(
    IOptions<RegistrationNotificationOptions> notificationOptions,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<RegistrationNotificationQueue> logger) : IRegistrationNotifier
{
    /// <summary>
    /// The most emails that can wait. Registrations are rate-limited and the worker sends each in a second or two, so a
    /// queue this long means Cloudflare has been failing for a while or sign-ups are flooding in: further emails are then
    /// dropped (and logged) rather than piled up in memory.
    /// </summary>
    public const int Capacity = 100;

    private readonly Channel<RegistrationNotice> _channel = Channel.CreateBounded<RegistrationNotice>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    /// <summary>Gets a value indicating whether registration emails are on: someone is to get them, and email is set up.</summary>
    public bool IsEnabled => notificationOptions.Value.Recipients.Count > 0 && emailOptions.Value.IsConfigured;

    /// <summary>Gets the waiting emails, for the worker.</summary>
    public ChannelReader<RegistrationNotice> Reader => _channel.Reader;

    /// <inheritdoc />
    public void NotifyRegistered(string displayName)
    {
        if (!IsEnabled)
        {
            return;
        }

        // TryWrite never waits: with the queue full it returns false instead.
        if (!_channel.Writer.TryWrite(new RegistrationNotice(displayName, timeProvider.GetUtcNow())))
        {
            RegistrationNotificationLog.Dropped(logger, Capacity);
        }
    }
}
