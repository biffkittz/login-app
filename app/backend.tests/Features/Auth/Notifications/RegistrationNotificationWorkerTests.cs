using System.Diagnostics.Metrics;
using LoginApp.Api.Features.Auth.Notifications;
using LoginApp.Api.Infrastructure.Email;
using LoginApp.Api.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LoginApp.Api.Tests.Features.Auth.Notifications;

/// <summary>
/// The background worker that sends registration emails: order, retries, the daily limit, the queue's limit and what
/// happens to waiting emails when the app stops. The clock is fake, so retry delays take no time.
/// </summary>
public sealed class RegistrationNotificationWorkerTests
{
    // RegistrationNotificationLog.DailyLimitReached.
    private const int DailyLimitReachedEventId = 1108;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Worker_SendsEachRegistrationInTurn()
    {
        // Arrange
        await using Harness harness = await Harness.StartAsync();

        // Act
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await harness.Sender.WaitForAttemptsAsync(2);

        // Assert
        Assert.Equal(["Ada", "Grace"], harness.Sender.Sent.Select(NameIn));
        Assert.All(harness.Sender.Sent, email => Assert.Equal(["owner@example.com"], email.To));
    }

    [Fact]
    public async Task Worker_TriesAgainAfterAPassingFailure()
    {
        // Arrange: Cloudflare is busy once, then takes the email.
        await using Harness harness = await Harness.StartAsync();
        int calls = 0;
        harness.Sender.Answer = (message, token) => Interlocked.Increment(ref calls) == 1
            ? throw new EmailDeliveryException("Cloudflare answered 503", isTransient: true)
            : FakeEmailSender.DeliverAsync(message, token);

        // Act
        harness.Queue.NotifyRegistered("Ada");
        await harness.Sender.WaitForAttemptsAsync(1);
        await harness.AdvanceUntilAsync(() => harness.Sender.Attempts == 2);

        // Assert
        Assert.Equal(["Ada", "Ada"], harness.Sender.Sent.Select(NameIn));
    }

    [Fact]
    public async Task Worker_GivesUpAfterThreeAttempts_AndCarriesOnWithTheNextEmail()
    {
        // Arrange: Cloudflare stays unreachable for Ada's email.
        await using Harness harness = await Harness.StartAsync();
        harness.Sender.Answer = (message, token) => NameIn(message) == "Ada"
            ? throw new EmailDeliveryException("Cloudflare's API couldn't be reached", isTransient: true)
            : FakeEmailSender.DeliverAsync(message, token);

        // Act
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await harness.AdvanceUntilAsync(() => harness.Sender.Attempts == 4);

        // Assert
        Assert.Equal(["Ada", "Ada", "Ada", "Grace"], harness.Sender.Sent.Select(NameIn));
    }

    [Fact]
    public async Task Worker_DoesNotRetryARefusedEmail()
    {
        // Arrange: a token without permission to send. Another try would only be refused again.
        await using Harness harness = await Harness.StartAsync();
        harness.Sender.Answer = (message, token) => NameIn(message) == "Ada"
            ? throw new EmailDeliveryException("Cloudflare answered 403 (10102)", isTransient: false)
            : FakeEmailSender.DeliverAsync(message, token);

        // Act
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await harness.Sender.WaitForAttemptsAsync(2);

        // Assert: Grace's email is the next attempt, without any retry delay in between.
        Assert.Equal(["Ada", "Grace"], harness.Sender.Sent.Select(NameIn));
    }

    [Fact]
    public async Task Worker_CountsEachEmailByOutcome()
    {
        // Arrange: Cloudflare refuses Ada's email and takes Grace's.
        await using Harness harness = await Harness.StartAsync();
        using MetricCollector<long> emails = new(harness.MeterFactory, ApplicationMeter.Name, "nibbles.registration_emails");
        harness.Sender.Answer = (message, token) => NameIn(message) == "Ada"
            ? throw new EmailDeliveryException("Cloudflare answered 403 (10102)", isTransient: false)
            : FakeEmailSender.DeliverAsync(message, token);

        // Act
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await emails.WaitForMeasurementsAsync(2, TimeSpan.FromSeconds(10));

        // Assert: the outcome is all the metric says about an email.
        Assert.Equal(
            [RegistrationNotificationMetrics.Failed, RegistrationNotificationMetrics.Sent],
            emails.GetMeasurementSnapshot().Select(measurement => (string)measurement.Tags[RegistrationNotificationMetrics.OutcomeTag]!));
        Assert.All(emails.GetMeasurementSnapshot(), measurement => Assert.Single(measurement.Tags));
    }

    [Fact]
    public async Task Worker_SurvivesAnUnexpectedFailure()
    {
        // Arrange
        await using Harness harness = await Harness.StartAsync();
        harness.Sender.Answer = (message, token) => NameIn(message) == "Ada"
            ? throw new InvalidOperationException("A bug")
            : FakeEmailSender.DeliverAsync(message, token);

        // Act
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await harness.Sender.WaitForAttemptsAsync(2);

        // Assert
        Assert.Equal(["Ada", "Grace"], harness.Sender.Sent.Select(NameIn));
    }

    [Fact]
    public async Task Worker_StopsAtTheDailyLimit_AndSaysSoInTheLastEmail()
    {
        // Arrange
        await using Harness harness = await Harness.StartAsync(maxPerDay: 2);

        // Act: four registrations, the last a day later.
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        harness.Queue.NotifyRegistered("Charles");
        await harness.WaitForLogAsync(DailyLimitReachedEventId);
        harness.Clock.Advance(TimeSpan.FromDays(1));
        harness.Queue.NotifyRegistered("Dorothy");
        await harness.Sender.WaitForAttemptsAsync(3);

        // Assert: Charles registered over the limit, so nobody was emailed about him.
        EmailMessage[] sent = [.. harness.Sender.Sent];
        Assert.Equal(["Ada", "Grace", "Dorothy"], sent.Select(NameIn));
        Assert.DoesNotContain("MaxPerDay", sent[0].Text, StringComparison.Ordinal);
        Assert.Contains("That makes 2 registration emails in 24 hours", sent[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_WhenTheAppStops_SendsWhatIsStillWaiting()
    {
        // Arrange: Ada's email is in flight when the app stops (it never finishes); Grace's is waiting behind it.
        await using Harness harness = await Harness.StartAsync();
        harness.Sender.Answer = async (message, token) =>
        {
            if (NameIn(message) == "Ada")
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return await FakeEmailSender.DeliverAsync(message, token);
        };
        harness.Queue.NotifyRegistered("Ada");
        harness.Queue.NotifyRegistered("Grace");
        await harness.Sender.WaitForAttemptsAsync(1);

        // Act
        await harness.Worker.StopAsync(CancellationToken);

        // Assert
        Assert.Equal(["Ada", "Grace"], harness.Sender.Sent.Select(NameIn));
    }

    [Fact]
    public async Task Queue_WhenFull_DropsFurtherEmailsInsteadOfWaiting()
    {
        // Arrange: no worker, so nothing leaves the queue.
        await using var harness = Harness.Create();

        // Act
        for (int i = 0; i <= RegistrationNotificationQueue.Capacity; i++)
        {
            harness.Queue.NotifyRegistered($"Person {i}");
        }

        // Assert
        Assert.Equal(RegistrationNotificationQueue.Capacity, harness.Queue.Reader.Count);
    }

    [Fact]
    public async Task Queue_WithoutRecipients_KeepsNothing()
    {
        // Arrange
        await using var harness = Harness.Create(recipients: []);

        // Act
        harness.Queue.NotifyRegistered("Ada");

        // Assert
        Assert.False(harness.Queue.IsEnabled);
        Assert.Equal(0, harness.Queue.Reader.Count);
    }

    [Fact]
    public async Task Queue_WithoutEmailConfigured_KeepsNothing()
    {
        // Arrange
        await using var harness = Harness.Create(email: new EmailOptions());

        // Act
        harness.Queue.NotifyRegistered("Ada");

        // Assert
        Assert.False(harness.Queue.IsEnabled);
        Assert.Equal(0, harness.Queue.Reader.Count);
    }

    // The name as the email quotes it: Name, as they typed it: "...".
    private static string NameIn(EmailMessage email)
    {
        const string Prefix = "Name, as they typed it: \"";
        int start = email.Text.IndexOf(Prefix, StringComparison.Ordinal) + Prefix.Length;
        return email.Text[start..email.Text.IndexOf('"', start)];
    }

    // The worker with a fake clock and a stand-in for Cloudflare.
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private Harness(IReadOnlyList<string> recipients, EmailOptions email, int maxPerDay)
        {
            IOptions<RegistrationNotificationOptions> options = Options.Create(new RegistrationNotificationOptions
            {
                Recipients = [.. recipients],
                MaxPerDay = maxPerDay,
            });
            IOptions<EmailOptions> emailOptions = Options.Create(email);
            _services = new ServiceCollection().AddMetrics().AddSingleton<IEmailSender>(Sender).BuildServiceProvider();
            Queue = new RegistrationNotificationQueue(options, emailOptions, Clock, NullLogger<RegistrationNotificationQueue>.Instance);
            Worker = new RegistrationNotificationWorker(
                Queue,
                _services.GetRequiredService<IServiceScopeFactory>(),
                new RegistrationNotificationBudget(Clock, options),
                new RegistrationNotificationMetrics(MeterFactory),
                options,
                emailOptions,
                Clock,
                Log);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 14, 3, 0, TimeSpan.Zero));

        public FakeEmailSender Sender { get; } = new();

        public IMeterFactory MeterFactory => _services.GetRequiredService<IMeterFactory>();

        public RegistrationNotificationQueue Queue { get; }

        public RegistrationNotificationWorker Worker { get; }

        public RecordingLogger<RegistrationNotificationWorker> Log { get; } = new();

        public static Harness Create(IReadOnlyList<string>? recipients = null, EmailOptions? email = null, int maxPerDay = 50) =>
            new(
                recipients ?? ["owner@example.com"],
                email ?? new EmailOptions
                {
                    AccountId = "0123456789abcdef0123456789abcdef",
                    ApiToken = "test-email-token",
                    SenderAddress = "notifications@example.com",
                },
                maxPerDay);

        public static async Task<Harness> StartAsync(int maxPerDay = 50)
        {
            Harness harness = Create(maxPerDay: maxPerDay);
            await harness.Worker.StartAsync(TestContext.Current.CancellationToken);
            return harness;
        }

        // Moves the fake clock on a second at a time, giving the worker a moment between steps, until the condition holds.
        public async Task AdvanceUntilAsync(Func<bool> condition)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            while (!condition())
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(TimeSpan.FromMilliseconds(5), linked.Token);
            }
        }

        // Waits until the worker has logged the event, failing the test after a few seconds.
        public async Task WaitForLogAsync(int eventId)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            while (!Log.EventIds.Contains(eventId))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), linked.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            Worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    // Keeps the event IDs of what is logged, so a test can wait for something the worker does that has no other trace.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> _eventIds = new();

        public IReadOnlyCollection<int> EventIds => _eventIds;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _eventIds.Enqueue(eventId.Id);
    }
}
