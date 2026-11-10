using System.Collections.Concurrent;
using LoginApp.Api.Infrastructure.Email;

namespace LoginApp.Api.Tests.Features.Auth.Notifications;

/// <summary>
/// Stands in for Cloudflare Email Service: keeps every email it is asked to send and answers as a test tells it to.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private int _attempts;

    /// <summary>
    /// Gets or sets how each request is answered; by default, delivered to every recipient. Tests that change it put the
    /// default back when done, because a factory (and so this instance) is shared by a test class.
    /// </summary>
    public Func<EmailMessage, CancellationToken, Task<EmailDelivery>> Answer { get; set; } = DeliverAsync;

    /// <summary>Gets the emails asked for so far, in order, including those whose answer was a failure.</summary>
    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    /// <summary>Gets how many times sending was attempted.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>The default answer: delivered to everyone.</summary>
    /// <param name="message">The email.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>The delivery.</returns>
    public static Task<EmailDelivery> DeliverAsync(EmailMessage message, CancellationToken cancellationToken) =>
        Task.FromResult(new EmailDelivery(message.To.Count, Queued: 0, Undeliverable: 0));

    /// <inheritdoc />
    public Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);
        _sent.Enqueue(message);
        return Answer(message, cancellationToken);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> emails have been asked for, failing the test after a few seconds.
    /// </summary>
    /// <param name="count">How many to wait for.</param>
    /// <returns>A task that completes when they have.</returns>
    public async Task WaitForAttemptsAsync(int count)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (Attempts < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), linked.Token);
        }
    }
}
