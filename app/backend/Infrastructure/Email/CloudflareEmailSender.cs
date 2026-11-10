using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// Sends email with Cloudflare Email Service's REST API (<c>POST accounts/{account}/email/sending/send</c>).
/// </summary>
/// <remarks>
/// <para>
/// The API token travels in the <c>Authorization</c> header. It is an account token that Terraform creates with the
/// Email Sending permission alone, so a leaked copy can send email from the domain but touch nothing else.
/// </para>
/// <para>
/// Cloudflare answers with which recipients it delivered to, queued or gave up on. Sending to the account's verified
/// destination addresses (the registration emails' recipients) is free on every plan.
/// </para>
/// </remarks>
/// <param name="httpClient">A client whose base address is <see cref="EmailOptions.Endpoint"/>.</param>
/// <param name="options">The account, token and sender.</param>
internal sealed partial class CloudflareEmailSender(HttpClient httpClient, IOptions<EmailOptions> options) : IEmailSender
{
    /// <summary>The most recipients one email can have: Cloudflare's limit across to, cc and bcc.</summary>
    public const int MaxRecipients = 50;

    // Cloudflare lists every problem with a request; a few say enough.
    private const int MaxErrorsDescribed = 3;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);
        EmailOptions settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException(
                $"Email isn't configured ({EmailOptions.SectionName}:AccountId, :ApiToken and :SenderAddress).");
        }

        if (message.To.Count is 0 or > MaxRecipients)
        {
            throw new ArgumentException($"An email goes to 1 to {MaxRecipients} recipients.", nameof(message));
        }

        // Serialized up front, so the request carries a Content-Length rather than a chunked body. It is a few hundred
        // bytes.
        SendRequest payload = new(
            From: new Sender(settings.SenderAddress!, settings.SenderName),
            To: message.To,
            Subject: message.Subject,
            Text: message.Text);
        ByteArrayContent body = new(JsonSerializer.SerializeToUtf8Bytes(payload, s_json));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri($"accounts/{Uri.EscapeDataString(settings.AccountId!)}/email/sending/send", UriKind.Relative))
        {
            Content = body,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            // No answer at all (DNS, a refused connection, TLS): asking again shortly may well work.
            throw new EmailDeliveryException("Cloudflare's API couldn't be reached", exception, isTransient: true);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout. Cloudflare may have taken the email anyway, so it isn't sent again: a second
            // copy is worse than an email that a timeout lost, which the log records.
            throw new EmailDeliveryException("Cloudflare's API didn't answer in time", exception, isTransient: false);
        }

        using (response)
        {
            using JsonDocument? answer = await ReadJsonAsync(response, cancellationToken);
            JsonElement root = answer?.RootElement ?? default;
            bool succeeded = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("success", out JsonElement success)
                && success.ValueKind == JsonValueKind.True;
            if (!response.IsSuccessStatusCode || !succeeded)
            {
                throw new EmailDeliveryException(
                    $"Cloudflare answered {(int)response.StatusCode}{DescribeErrors(root)}",
                    IsTransient(response.StatusCode));
            }

            JsonElement result = root.TryGetProperty("result", out JsonElement value) ? value : default;
            return new EmailDelivery(
                Delivered: CountOf(result, "delivered"),
                Queued: CountOf(result, "queued"),
                Undeliverable: CountOf(result, "permanent_bounces") + CountOf(result, "suppressed_recipients"));
        }
    }

    // Rate limits and Cloudflare's own failures pass; a refused request (bad token, no permission, sending disabled, an
    // invalid email) doesn't.
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    // Null when the answer isn't JSON, such as a proxy's HTML error page.
    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(body, default, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Cloudflare's errors look like {"code": 10102, "message": "email.sending.error.authorization.forbidden"}: a number
    // and a dotted identifier. Only those shapes are kept, so free text that might quote the request is never logged.
    private static string DescribeErrors(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("errors", out JsonElement errors)
            || errors.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        StringBuilder description = new();
        foreach (JsonElement error in errors.EnumerateArray().Take(MaxErrorsDescribed))
        {
            if (error.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? code = error.TryGetProperty("code", out JsonElement number) && number.TryGetInt32(out int value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : null;
            string? text = error.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is string identifier
                && ErrorIdentifier().IsMatch(identifier)
                    ? identifier
                    : null;
            string described = string.Join(' ', new[] { code, text }.OfType<string>());
            if (described.Length > 0)
            {
                description.Append(description.Length == 0 ? " (" : "; ").Append(described);
            }
        }

        return description.Length == 0 ? string.Empty : description.Append(')').ToString();
    }

    private static int CountOf(JsonElement result, string name) =>
        result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty(name, out JsonElement list)
        && list.ValueKind == JsonValueKind.Array
            ? list.GetArrayLength()
            : 0;

    [GeneratedRegex("^[a-z0-9_.]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorIdentifier();

    // {"from": {"address": ..., "name": ...}, "to": [...], "subject": ..., "text": ...}
    private sealed record SendRequest(Sender From, IReadOnlyList<string> To, string Subject, string Text);

    private sealed record Sender(string Address, string Name);
}
