using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Draws grocery pictures with the Gemini API's <c>generateContent</c> method.
/// </summary>
/// <remarks>
/// <para>
/// Only the item's name leaves the app: not who added it, nor its store, brand or notes. The API key travels in the
/// <c>x-goog-api-key</c> header rather than the URL's <c>key</c> parameter, where proxies and logs would record it.
/// </para>
/// <para>
/// The picture comes back as one megabyte or so of Base64 PNG in a JSON response, which the HTTP client buffers
/// (bounded by its <c>MaxResponseContentBufferSize</c>). <see cref="GroceryImageThumbnails"/> then shrinks it to the
/// few kilobytes the app stores and serves.
/// </para>
/// </remarks>
/// <param name="httpClient">A client whose base address is <see cref="ImageGenerationOptions.Endpoint"/>.</param>
/// <param name="options">The model and API key.</param>
internal sealed partial class GeminiImageGenerator(HttpClient httpClient, IOptions<ImageGenerationOptions> options)
    : IGroceryImageGenerator
{
    /// <summary>The request header that carries the API key.</summary>
    public const string ApiKeyHeader = "x-goog-api-key";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task<byte[]> GenerateAsync(string itemName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImageGenerationOptions settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException($"No Gemini API key is configured ({ImageGenerationOptions.SectionName}:ApiKey).");
        }

        // Serialized up front, so the request carries a Content-Length rather than a chunked body, which some proxies
        // refuse. It is a few hundred bytes.
        ByteArrayContent body = new(JsonSerializer.SerializeToUtf8Bytes(CreateRequest(itemName), s_json));
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"models/{settings.Model}:generateContent", UriKind.Relative))
        {
            Content = body,
        };
        request.Headers.Add(ApiKeyHeader, settings.ApiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string status = await ReadErrorStatusAsync(response, cancellationToken);
            throw new ImageGenerationException(
                $"Gemini answered {(int)response.StatusCode} {status}".TrimEnd(),
                IsTransient(response.StatusCode));
        }

        await using Stream answer = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(answer, default, cancellationToken);
        return FindImage(document.RootElement)
            ?? throw new ImageGenerationException($"Gemini drew no picture ({DescribeMissingImage(document.RootElement)})", isTransient: false);
    }

    /// <summary>
    /// Writes the instructions for the picture of <paramref name="itemName"/>.
    /// </summary>
    /// <param name="itemName">The item's name.</param>
    /// <returns>The prompt.</returns>
    /// <remarks>
    /// The picture is shown 40 pixels wide next to the name, so it asks for an icon: one subject filling the frame on
    /// a plain background in the app's warm colors, and no text, which would be illegible at that size anyway. The name
    /// is quoted, to mark it as the thing to draw rather than more instructions.
    /// </remarks>
    internal static string Prompt(string itemName) =>
        "Draw a friendly, flat illustration of this grocery item, to be used as its icon in a family shopping list app: "
        + $"\"{itemName.Trim()}\". Show only the item, centered and filling most of the square frame, on a plain warm "
        + "cream background, in soft mid-century colors. No text, letters, labels, logos, people or hands.";

    private static GenerateContentRequest CreateRequest(string itemName) =>
        new(
            Contents: [new Content([new TextPart(Prompt(itemName))])],
            GenerationConfig: new GenerationConfig(ResponseModalities: ["IMAGE"], ImageConfig: new ImageConfig(AspectRatio: "1:1")));

    // Rate limits (429) and overload (503 "model is overloaded" is common at busy times) pass; a refused request doesn't.
    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    // The final picture is the last image part that isn't a "thought": Gemini 3 image models may sketch interim images
    // while they think, and mark those parts with "thought": true.
    private static byte[]? FindImage(JsonElement root)
    {
        string? data = null;
        foreach (JsonElement part in Parts(root))
        {
            bool isThought = part.TryGetProperty("thought", out JsonElement thought) && thought.ValueKind == JsonValueKind.True;
            if (!isThought
                && TryGetObject(part, "inlineData", "inline_data", out JsonElement inlineData)
                && TryGetString(inlineData, "mimeType", "mime_type") is string mimeType
                && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && TryGetString(inlineData, "data", "data") is string base64)
            {
                data = base64;
            }
        }

        if (data is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException exception)
        {
            throw new ImageGenerationException("Gemini's picture wasn't valid Base64", exception);
        }
    }

    private static IEnumerable<JsonElement> Parts(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            if (TryGetObject(candidate, "content", "content", out JsonElement content)
                && content.TryGetProperty("parts", out JsonElement parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object)
                    {
                        yield return part;
                    }
                }
            }
        }
    }

    // Why there was no picture: the first candidate's finish reason (IMAGE_SAFETY, NO_IMAGE, ...) or, when the prompt
    // itself was blocked, the block reason. Both are enum names; anything else isn't logged.
    private static string DescribeMissingImage(JsonElement root)
    {
        string? reason = null;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("candidates", out JsonElement candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0
                && candidates[0].ValueKind == JsonValueKind.Object)
            {
                reason = TryGetString(candidates[0], "finishReason", "finish_reason");
            }

            if (reason is null && TryGetObject(root, "promptFeedback", "prompt_feedback", out JsonElement feedback))
            {
                reason = TryGetString(feedback, "blockReason", "block_reason");
            }
        }

        return reason is not null && EnumName().IsMatch(reason) ? $"reason {reason}" : "no reason given";
    }

    // Gemini's errors look like {"error": {"code": 429, "message": "...", "status": "RESOURCE_EXHAUSTED"}}. Only the
    // status name is kept: the message is free text that can quote the request.
    private static async Task<string> ReadErrorStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(body, default, cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && TryGetObject(document.RootElement, "error", "error", out JsonElement error)
                && TryGetString(error, "status", "status") is string status
                && EnumName().IsMatch(status))
            {
                return status;
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's HTML error page, say): the status code alone will have to do.
        }

        return string.Empty;
    }

    // The REST API answers in camelCase, but protobuf's JSON mapping also allows the snake_case field names, so both
    // are accepted.
    private static bool TryGetObject(JsonElement element, string name, string alternativeName, out JsonElement value) =>
        (element.TryGetProperty(name, out value) || element.TryGetProperty(alternativeName, out value))
        && value.ValueKind == JsonValueKind.Object;

    private static string? TryGetString(JsonElement element, string name, string alternativeName) =>
        (element.TryGetProperty(name, out JsonElement value) || element.TryGetProperty(alternativeName, out value))
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex("^[A-Z][A-Z_]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnumName();

    private sealed record GenerateContentRequest(IReadOnlyList<Content> Contents, GenerationConfig GenerationConfig);

    private sealed record Content(IReadOnlyList<TextPart> Parts);

    private sealed record TextPart(string Text);

    // "IMAGE" alone: the model answers with the picture and no commentary to skip over.
    private sealed record GenerationConfig(IReadOnlyList<string> ResponseModalities, ImageConfig ImageConfig);

    private sealed record ImageConfig(string AspectRatio);
}
