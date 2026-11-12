using LoginApp.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Sends the registration emails that <see cref="RegistrationNotificationQueue"/> holds, one at a time, in the
/// background, so registering never waits for Cloudflare.
/// </summary>
/// <remarks>
/// A failed email is tried twice more when the failure looks passing (Cloudflare unreachable, busy or rate limiting),
/// then given up on and logged. It never affects the account, which exists already.
/// </remarks>
/// <param name="queue">The waiting emails.</param>
/// <param name="scopeFactory">Resolves the email sender per email, as typed HTTP clients are meant to be used.</param>
/// <param name="budget">The daily limit.</param>
/// <param name="metrics">Counts the emails by outcome.</param>
/// <param name="options">The recipients, subject and limit.</param>
/// <param name="emailOptions">Whether email is set up, to explain why emails are off.</param>
/// <param name="timeProvider">The clock, for delays.</param>
/// <param name="logger">The logger.</param>
internal sealed class RegistrationNotificationWorker(
    RegistrationNotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    RegistrationNotificationBudget budget,
    RegistrationNotificationMetrics metrics,
    IOptions<RegistrationNotificationOptions> options,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider,
    ILogger<RegistrationNotificationWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    // Doubles with every attempt: 5 seconds, then 10.
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(5);

    // When the app stops, what is still queued gets this long to go out. The host allows 30 seconds for shutting down.
    private static readonly TimeSpan s_shutdownGrace = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!queue.IsEnabled)
        {
            if (options.Value.Recipients.Count == 0)
            {
                RegistrationNotificationLog.OffWithoutRecipients(logger);
            }
            else if (!emailOptions.Value.IsConfigured)
            {
                RegistrationNotificationLog.OffWithoutEmail(logger);
            }

            return;
        }

        RegistrationNotificationLog.On(logger, options.Value.Recipients.Count);
        try
        {
            await foreach (RegistrationNotice notice in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendAsync(notice, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is stopping. Whatever is still waiting gets a last chance below.
        }

        await SendRemainingAsync();
    }

    private async Task SendRemainingAsync()
    {
        using CancellationTokenSource grace = new(s_shutdownGrace, timeProvider);
        try
        {
            while (queue.Reader.TryRead(out RegistrationNotice? notice))
            {
                await SendAsync(notice, grace.Token);
            }
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            // Out of time; what is left is counted below.
        }

        if (queue.Reader.Count > 0)
        {
            RegistrationNotificationLog.LostAtShutdown(logger, queue.Reader.Count);
            metrics.Emails(RegistrationNotificationMetrics.LostAtShutdown, queue.Reader.Count);
        }
    }

    private async Task SendAsync(RegistrationNotice notice, CancellationToken cancellationToken)
    {
        RegistrationNotificationOptions settings = options.Value;
        if (!budget.TryTake(out int sentInWindow))
        {
            RegistrationNotificationLog.DailyLimitReached(logger, settings.MaxPerDay);
            metrics.Emails(RegistrationNotificationMetrics.DailyLimit);
            return;
        }

        EmailMessage message = RegistrationEmail.Create(notice, settings, isLastForToday: sentInWindow == settings.MaxPerDay);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                EmailDelivery delivery = await sender.SendAsync(message, cancellationToken);
                RegistrationNotificationLog.Sent(logger, message.To.Count, delivery.Delivered, delivery.Queued);
                metrics.Emails(RegistrationNotificationMetrics.Sent);
                if (delivery.Undeliverable > 0)
                {
                    RegistrationNotificationLog.Undeliverable(logger, delivery.Undeliverable);
                }

                return;
            }
            catch (EmailDeliveryException exception)
                when (exception.IsTransient && attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                RegistrationNotificationLog.Retrying(logger, attempt, exception.Message);
                await Task.Delay(s_retryDelay * Math.Pow(2, attempt - 1), timeProvider, cancellationToken);
            }
            catch (EmailDeliveryException exception)
            {
                RegistrationNotificationLog.Failed(logger, exception.Message);
                metrics.Emails(RegistrationNotificationMetrics.Failed);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Anything else (an unreadable answer, a bug) costs this one email, not the worker: an exception escaping
                // a BackgroundService stops the whole app, list and all.
                RegistrationNotificationLog.FailedUnexpectedly(logger, exception);
                metrics.Emails(RegistrationNotificationMetrics.Failed);
                return;
            }
        }
    }
}
