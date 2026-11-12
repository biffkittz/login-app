using System.Text;
using System.Threading.Channels;
using LoginApp.Api.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace LoginApp.Api.Tests.Infrastructure.Telemetry;

/// <summary>
/// Pushing metrics to Prometheus' OTLP receiver, against a stand-in receiver on this machine.
/// </summary>
public sealed class MetricsExportTests
{
    private const string Username = "app";
    private const string Password = "test-otlp-password";
    private const string Site = "app.example.test";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Metrics_ArePushedAsOtlpProtobuf_WithBasicAuthentication_AndTheSite()
    {
        // Arrange
        await using OtlpReceiver receiver = await OtlpReceiver.StartAsync();
        await using ExportingApiFactory factory = new(receiver.MetricsUri);
        await factory.InitializeAsync();
        using HttpClient client = factory.CreateBrowserLikeClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/grocery-list", UriKind.Relative), CancellationToken);

        // Act
        Assert.True(factory.Services.GetRequiredService<MeterProvider>().ForceFlush((int)s_timeout.TotalMilliseconds));
        ReceivedExport export = await receiver.Exports.Reader.ReadAsync(CancellationToken).AsTask().WaitAsync(s_timeout, CancellationToken);

        // Assert
        Assert.Equal("/api/v1/otlp/v1/metrics", export.Path);
        Assert.Equal("application/x-protobuf", export.ContentType);
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"))}", export.Authorization);

        // Protobuf keeps strings as UTF-8, so names and attribute values can be found in the raw body.
        string body = Encoding.UTF8.GetString(export.Body);
        Assert.Contains(TelemetryServiceCollectionExtensions.ServiceName, body, StringComparison.Ordinal);
        Assert.Contains(Site, body, StringComparison.Ordinal);
        Assert.Contains("http.server.request.duration", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnEndpoint_NothingIsExported()
    {
        // Arrange
        await using LoginApiFactory factory = new();
        await factory.InitializeAsync();

        // Act
        MeterProvider? provider = factory.Services.GetService<MeterProvider>();

        // Assert: no pipeline at all, so nothing is collected for nobody.
        Assert.Null(provider);
    }

    private sealed record ReceivedExport(string Path, string? ContentType, string? Authorization, byte[] Body);

    // A LoginApiFactory whose app exports metrics to the receiver.
    private sealed class ExportingApiFactory(Uri endpoint) : LoginApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Telemetry:OtlpEndpoint", endpoint.AbsoluteUri);
            builder.UseSetting("Telemetry:OtlpUsername", Username);
            builder.UseSetting("Telemetry:OtlpPassword", Password);
            builder.UseSetting("Telemetry:Site", Site);

            // Only the forced flush exports during the test.
            builder.UseSetting("Telemetry:ExportIntervalSeconds", "300");
        }
    }

    // Plays Prometheus' OTLP receiver on a free local port and hands over every export it receives.
    private sealed class OtlpReceiver : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private OtlpReceiver(WebApplication app, Uri baseUri)
        {
            _app = app;
            MetricsUri = new Uri(baseUri, "/api/v1/otlp/v1/metrics");
        }

        public Uri MetricsUri { get; }

        public Channel<ReceivedExport> Exports { get; } = Channel.CreateUnbounded<ReceivedExport>();

        public static async Task<OtlpReceiver> StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            WebApplication app = builder.Build();
            OtlpReceiver? receiver = null;
            app.MapPost("/api/v1/otlp/v1/metrics", async (HttpRequest request) =>
            {
                using MemoryStream body = new();
                await request.Body.CopyToAsync(body);
                receiver!.Exports.Writer.TryWrite(new ReceivedExport(
                    request.Path,
                    request.ContentType,
                    request.Headers.Authorization.ToString(),
                    body.ToArray()));
                return Results.Ok();
            });
            await app.StartAsync(CancellationToken);
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            receiver = new OtlpReceiver(app, new Uri(address));
            return receiver;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
