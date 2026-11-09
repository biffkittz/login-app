using System.Net;
using System.Text;
using System.Text.Json;
using LoginApp.Api.Features.GroceryList.Images;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// The Gemini client against a recorded-style stand-in for the API: what it sends, and how it reads the answers and
/// failures Gemini gives.
/// </summary>
public sealed class GeminiImageGeneratorTests
{
    private const string ApiKey = "test-key-123";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GenerateAsync_SendsOnlyTheNameAndTheKeyInAHeader()
    {
        // Arrange
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, ImageResponse(Base64Of([1, 2, 3]))));
        GeminiImageGenerator generator = CreateGenerator(handler);

        // Act
        await generator.GenerateAsync("Oat milk", CancellationToken);

        // Assert
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.True(handler.ContentLengths.Single() > 0, "The body must have a Content-Length, not be sent in chunks.");
        Assert.Equal("https://gemini.test/v1/models/gemini-3.1-flash-lite-image:generateContent", request.RequestUri?.ToString());
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues(GeminiImageGenerator.ApiKeyHeader)));
        Assert.DoesNotContain(ApiKey, request.RequestUri?.ToString(), StringComparison.Ordinal);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        JsonElement root = body.RootElement;
        string prompt = root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("\"Oat milk\"", prompt, StringComparison.Ordinal);
        JsonElement config = root.GetProperty("generationConfig");
        Assert.Equal("IMAGE", Assert.Single(config.GetProperty("responseModalities").EnumerateArray()).GetString());
        Assert.Equal("1:1", config.GetProperty("imageConfig").GetProperty("aspectRatio").GetString());
    }

    [Fact]
    public async Task GenerateAsync_ReturnsTheFinalPicture_NotTheModelsDraftsOrText()
    {
        // Arrange: a draft ("thought") image, some text, then the final image, as Gemini 3 models may answer.
        string response = $$"""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "thought": true, "inlineData": { "mimeType": "image/png", "data": "{{Base64Of([9, 9])}}" } },
                    { "text": "Here is your picture." },
                    { "inlineData": { "mimeType": "image/png", "data": "{{Base64Of([1, 2, 3])}}" }, "thoughtSignature": "abc" }
                  ]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        GeminiImageGenerator generator = CreateGenerator(new StubHandler(_ => Json(HttpStatusCode.OK, response)));

        // Act
        byte[] picture = await generator.GenerateAsync("Bananas", CancellationToken);

        // Assert
        Assert.Equal([1, 2, 3], picture);
    }

    [Fact]
    public async Task GenerateAsync_AcceptsSnakeCaseFieldNames()
    {
        // Arrange
        string response = $$"""
            { "candidates": [{ "content": { "parts": [{ "inline_data": { "mime_type": "image/jpeg", "data": "{{Base64Of([4, 5])}}" } }] } }] }
            """;
        GeminiImageGenerator generator = CreateGenerator(new StubHandler(_ => Json(HttpStatusCode.OK, response)));

        // Act
        byte[] picture = await generator.GenerateAsync("Bananas", CancellationToken);

        // Assert
        Assert.Equal([4, 5], picture);
    }

    [Theory]
    [InlineData("""{ "candidates": [{ "content": { "parts": [{ "text": "I can't draw that." }] }, "finishReason": "IMAGE_SAFETY" }] }""", "IMAGE_SAFETY")]
    [InlineData("""{ "promptFeedback": { "blockReason": "PROHIBITED_CONTENT" } }""", "PROHIBITED_CONTENT")]
    [InlineData("""{ "candidates": [{ "finishReason": "Something odd; with spaces" }] }""", "no reason given")]
    public async Task GenerateAsync_WhenNoPictureComesBack_ThrowsWithTheReason(string response, string expectedReason)
    {
        // Arrange
        GeminiImageGenerator generator = CreateGenerator(new StubHandler(_ => Json(HttpStatusCode.OK, response)));

        // Act
        ImageGenerationException exception = await Assert.ThrowsAsync<ImageGenerationException>(
            () => generator.GenerateAsync("Secret family recipe", CancellationToken));

        // Assert
        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret family recipe", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", true)]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL", true)]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", false)]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED", false)]
    public async Task GenerateAsync_WhenTheApiRefuses_SaysWhetherAskingAgainMayHelp(HttpStatusCode status, string errorStatus, bool transient)
    {
        // Arrange: Gemini's error messages are free text that can quote the request; they must not reach the log.
        string response = $$"""{ "error": { "code": {{(int)status}}, "message": "Prompt 'Secret family recipe' failed", "status": "{{errorStatus}}" } }""";
        GeminiImageGenerator generator = CreateGenerator(new StubHandler(_ => Json(status, response)));

        // Act
        ImageGenerationException exception = await Assert.ThrowsAsync<ImageGenerationException>(
            () => generator.GenerateAsync("Secret family recipe", CancellationToken));

        // Assert
        Assert.Equal(transient, exception.IsTransient);
        Assert.Contains($"{(int)status} {errorStatus}", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_WhenAProxyAnswersWithHtml_ReportsTheStatusCode()
    {
        // Arrange
        GeminiImageGenerator generator = CreateGenerator(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html"),
        }));

        // Act
        ImageGenerationException exception = await Assert.ThrowsAsync<ImageGenerationException>(
            () => generator.GenerateAsync("Bananas", CancellationToken));

        // Assert
        Assert.Equal("Gemini answered 502", exception.Message);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task GenerateAsync_WithoutAnApiKey_RefusesToCallTheApi()
    {
        // Arrange
        StubHandler handler = new(_ => throw new InvalidOperationException("Must not be called."));
        GeminiImageGenerator generator = CreateGenerator(handler, apiKey: null);

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync("Bananas", CancellationToken));
        Assert.Empty(handler.Requests);
    }

    private static GeminiImageGenerator CreateGenerator(StubHandler handler, string? apiKey = ApiKey) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test/v1/") },
            Options.Create(new ImageGenerationOptions { ApiKey = apiKey }));

    private static string Base64Of(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string ImageResponse(string base64) =>
        $$"""{ "candidates": [{ "content": { "parts": [{ "inlineData": { "mimeType": "image/png", "data": "{{base64}}" } }] } }] }""";

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
