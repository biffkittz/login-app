using System.Net;
using System.Text;
using System.Text.Json;
using LoginApp.Api.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Tests.Infrastructure.Email;

/// <summary>
/// The Cloudflare Email Service client against a stand-in for the API: what it sends, and how it reads Cloudflare's
/// answers and failures. Nothing here reaches Cloudflare.
/// </summary>
public sealed class CloudflareEmailSenderTests
{
    private const string AccountId = "0123456789abcdef0123456789abcdef";
    private const string ApiToken = "test-email-token-123";

    private static readonly EmailMessage s_message = new(
        ["owner@example.com", "second@example.com"],
        "[grocery app] new registration detected",
        "Someone has just registered.\n");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_PostsThePlainTextEmailFromTheSenderWithTheTokenInTheAuthorizationHeader()
    {
        // Arrange
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Accepted(delivered: ["owner@example.com", "second@example.com"])));
        CloudflareEmailSender sender = CreateSender(handler);

        // Act
        await sender.SendAsync(s_message, CancellationToken);

        // Assert
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://cloudflare.test/client/v4/accounts/{AccountId}/email/sending/send", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(ApiToken, request.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(ApiToken, request.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.True(handler.ContentLengths.Single() > 0, "The body must have a Content-Length, not be sent in chunks.");

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        JsonElement root = body.RootElement;
        Assert.Equal("notifications@example.com", root.GetProperty("from").GetProperty("address").GetString());
        Assert.Equal("Nibbles", root.GetProperty("from").GetProperty("name").GetString());
        Assert.Equal(["owner@example.com", "second@example.com"], root.GetProperty("to").EnumerateArray().Select(to => to.GetString()));
        Assert.Equal(s_message.Subject, root.GetProperty("subject").GetString());
        Assert.Equal(s_message.Text, root.GetProperty("text").GetString());
        Assert.False(root.TryGetProperty("html", out _), "The email is plain text only.");
    }

    [Fact]
    public async Task SendAsync_ReportsDeliveredQueuedAndUndeliverableRecipients()
    {
        // Arrange
        string answer = """
            {
              "success": true, "errors": [], "messages": [],
              "result": {
                "delivered": ["owner@example.com"],
                "queued": ["second@example.com", "third@example.com"],
                "permanent_bounces": ["gone@example.com"],
                "suppressed_recipients": ["blocked@example.com"],
                "message_id": "<abc@example.com>"
              }
            }
            """;
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => Json(HttpStatusCode.OK, answer)));

        // Act
        EmailDelivery delivery = await sender.SendAsync(s_message, CancellationToken);

        // Assert
        Assert.Equal(new EmailDelivery(Delivered: 1, Queued: 2, Undeliverable: 2), delivery);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)] // no permission to send, or sending disabled
    [InlineData(HttpStatusCode.BadRequest, false)] // an email Cloudflare won't take
    [InlineData(HttpStatusCode.Unauthorized, false)] // a revoked token
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task SendAsync_WhenRefused_SaysWhetherAnotherTryMayWork(HttpStatusCode status, bool expectedTransient)
    {
        // Arrange
        string answer = """{ "success": false, "errors": [{ "code": 10102, "message": "email.sending.error.authorization.forbidden" }], "result": null }""";
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => Json(status, answer)));

        // Act
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));

        // Assert
        Assert.Equal(expectedTransient, exception.IsTransient);
        Assert.Equal(
            $"Cloudflare answered {(int)status} (10102 email.sending.error.authorization.forbidden)",
            exception.Message);
    }

    [Fact]
    public async Task SendAsync_KeepsFreeTextAndAddressesInErrorsOutOfTheMessage()
    {
        // Arrange: messages that aren't Cloudflare's dotted identifiers could quote the request.
        string answer = """
            {
              "success": false,
              "errors": [
                { "code": 10001, "message": "Recipient owner@example.com is not a verified destination" },
                { "code": 10202, "message": "email.sending.error.invalid_content" }
              ]
            }
            """;
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => Json(HttpStatusCode.BadRequest, answer)));

        // Act
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));

        // Assert
        Assert.Equal("Cloudflare answered 400 (10001; 10202 email.sending.error.invalid_content)", exception.Message);
        Assert.DoesNotContain("@", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithAnAnswerThatIsNotJson_FailsWithTheStatusAlone()
    {
        // Arrange: a proxy's error page.
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html><body>Bad gateway</body></html>", Encoding.UTF8, "text/html"),
        }));

        // Act
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));

        // Assert
        Assert.Equal("Cloudflare answered 502", exception.Message);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task SendAsync_WithSuccessFalseInAnOkAnswer_Fails()
    {
        // Arrange
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => Json(HttpStatusCode.OK, """{ "success": false, "errors": [] }""")));

        // Act + Assert
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task SendAsync_WhenCloudflareCannotBeReached_FailsTransiently()
    {
        // Arrange
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => throw new HttpRequestException("Name or service not known")));

        // Act
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));

        // Assert
        Assert.True(exception.IsTransient);
        Assert.Equal("Cloudflare's API couldn't be reached", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheClientTimesOut_DoesNotOfferAnotherTry()
    {
        // Arrange: Cloudflare may have taken the email before the timeout, so sending it again could double it.
        CloudflareEmailSender sender = CreateSender(new StubHandler(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout")));

        // Act
        EmailDeliveryException exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => sender.SendAsync(s_message, CancellationToken));

        // Assert
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task SendAsync_WithoutConfiguration_SendsNothing()
    {
        // Arrange
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Accepted(delivered: [])));
        CloudflareEmailSender sender = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://cloudflare.test/client/v4/") },
            Options.Create(new EmailOptions()));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(s_message, CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task SendAsync_WithNoneOrTooManyRecipients_SendsNothing(int recipientCount)
    {
        // Arrange
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, Accepted(delivered: [])));
        CloudflareEmailSender sender = CreateSender(handler);
        EmailMessage message = s_message with { To = [.. Enumerable.Range(0, recipientCount).Select(index => $"person{index}@example.com")] };

        // Act + Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync(message, CancellationToken));
        Assert.Empty(handler.Requests);
    }

    private static CloudflareEmailSender CreateSender(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://cloudflare.test/client/v4/") },
            Options.Create(new EmailOptions
            {
                AccountId = AccountId,
                ApiToken = ApiToken,
                SenderAddress = "notifications@example.com",
            }));

    private static string Accepted(string[] delivered) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            errors = Array.Empty<object>(),
            messages = Array.Empty<object>(),
            result = new { delivered, queued = Array.Empty<string>(), permanent_bounces = Array.Empty<string>() },
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Answers every request with the given function and keeps what was sent.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        // As sent: reading the body below would buffer it and give any content a length.
        public List<long?> ContentLengths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            ContentLengths.Add(request.Content?.Headers.ContentLength);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer(request);
        }
    }
}
