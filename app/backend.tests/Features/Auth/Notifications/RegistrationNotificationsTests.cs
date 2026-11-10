using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.Auth.Notifications;
using LoginApp.Api.Infrastructure.Email;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LoginApp.Api.Tests.Features.Auth.Notifications;

/// <summary>
/// Registering through the real API, with registration emails on: who hears about it, and what they are told.
/// </summary>
/// <remarks>
/// Tests in this class share one app and its stand-in for Cloudflare and run one after another, so each looks only at
/// the emails asked for since it started. The worker sends them in turn, so the first new email is about the first new
/// registration.
/// </remarks>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class RegistrationNotificationsTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_EmailsTheRecipientsWhereWhenAndUnderWhatName_ButNotTheEmailAddress()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();
        int before = factory.Sender.Attempts;
        string email = TestAccounts.UniqueEmail();

        // Act
        using HttpResponseMessage response = await TestAccounts.RegisterAsync(client, email, displayName: "Ada Lovelace");
        await factory.Sender.WaitForAttemptsAsync(before + 1);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        EmailMessage sent = factory.Sender.Sent.ElementAt(before);
        Assert.Equal([NotificationApiFactory.Recipient], sent.To);
        Assert.Equal("[grocery app] new registration detected", sent.Subject);
        Assert.Contains($"on {NotificationApiFactory.SiteUrl}.", sent.Text, StringComparison.Ordinal);
        Assert.Contains("Name, as they typed it: \"Ada Lovelace\"", sent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(email, sent.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WhenRejected_EmailsNobody()
    {
        // Arrange: an account whose address the duplicate below tries to take again.
        using HttpClient firstClient = factory.CreateBrowserLikeClient();
        using HttpClient weakPasswordClient = factory.CreateBrowserLikeClient();
        using HttpClient duplicateClient = factory.CreateBrowserLikeClient();
        using HttpClient acceptedClient = factory.CreateBrowserLikeClient();
        string takenEmail = TestAccounts.UniqueEmail();
        int start = factory.Sender.Attempts;
        using HttpResponseMessage first = await TestAccounts.RegisterAsync(firstClient, takenEmail, displayName: "First Account");
        await factory.Sender.WaitForAttemptsAsync(start + 1);
        int before = factory.Sender.Attempts;

        // Act: a password that's too short, then an address that's taken, then a registration that works.
        using HttpResponseMessage weakPassword = await TestAccounts.RegisterAsync(
            weakPasswordClient, TestAccounts.UniqueEmail(), password: "short", displayName: "Weak Password");
        using HttpResponseMessage duplicate = await TestAccounts.RegisterAsync(duplicateClient, takenEmail, displayName: "Same Address");
        using HttpResponseMessage accepted = await TestAccounts.RegisterAsync(
            acceptedClient, TestAccounts.UniqueEmail(), displayName: "Grace Hopper");
        await factory.Sender.WaitForAttemptsAsync(before + 1);

        // Assert: the first email after the rejections is about the registration that worked.
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Contains("\"Grace Hopper\"", factory.Sender.Sent.ElementAt(before).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_WhenTheEmailCannotBeSent_StillCreatesTheAccount()
    {
        // Arrange: Cloudflare refuses every email for now.
        using HttpClient client = factory.CreateBrowserLikeClient();
        int before = factory.Sender.Attempts;
        factory.Sender.Answer = (_, _) => throw new EmailDeliveryException("Cloudflare answered 403 (10102)", isTransient: false);
        try
        {
            // Act
            using HttpResponseMessage response = await TestAccounts.RegisterAsync(client, TestAccounts.UniqueEmail(), displayName: "Katherine Johnson");
            await factory.Sender.WaitForAttemptsAsync(before + 1);

            // Assert: signed in, whatever became of the email.
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using HttpResponseMessage me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            factory.Sender.Answer = FakeEmailSender.DeliverAsync;
        }

        // And the next registration is emailed as usual.
        using HttpClient laterClient = factory.CreateBrowserLikeClient();
        int next = factory.Sender.Attempts;
        using HttpResponseMessage later = await TestAccounts.RegisterAsync(laterClient, TestAccounts.UniqueEmail(), displayName: "Dorothy Vaughan");
        await factory.Sender.WaitForAttemptsAsync(next + 1);
        Assert.Contains("\"Dorothy Vaughan\"", factory.Sender.Sent.ElementAt(next).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_DoesNotWaitForTheEmail()
    {
        // Arrange: Cloudflare takes its time.
        using HttpClient client = factory.CreateBrowserLikeClient();
        int before = factory.Sender.Attempts;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Sender.Answer = async (message, token) =>
        {
            await release.Task.WaitAsync(token);
            return await FakeEmailSender.DeliverAsync(message, token);
        };
        try
        {
            // Act
            using HttpResponseMessage response = await TestAccounts.RegisterAsync(client, TestAccounts.UniqueEmail(), displayName: "Mary Jackson");

            // Assert: the account came back while the email is still on its way.
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await factory.Sender.WaitForAttemptsAsync(before + 1);
        }
        finally
        {
            release.SetResult();
            factory.Sender.Answer = FakeEmailSender.DeliverAsync;
        }
    }

    [Fact]
    public void Configuration_FromSettings_TurnsRegistrationEmailsOn() =>
        Assert.True(factory.Services.GetRequiredService<RegistrationNotificationQueue>().IsEnabled);
}

/// <summary>
/// Registering with registration emails off, as on a developer's machine: nothing is queued or sent.
/// </summary>
/// <param name="factory">An API host without email configured.</param>
public sealed class RegistrationNotificationsOffTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    [Fact]
    public async Task Register_WithoutRecipients_QueuesNoEmail()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();
        RegistrationNotificationQueue queue = factory.Services.GetRequiredService<RegistrationNotificationQueue>();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { displayName = "Ada", email = TestAccounts.UniqueEmail(), password = TestAccounts.ValidPassword },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(queue.IsEnabled);
        Assert.Equal(0, queue.Reader.Count);
    }
}
