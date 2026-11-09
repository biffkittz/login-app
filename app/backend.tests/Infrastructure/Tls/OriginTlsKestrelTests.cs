using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LoginApp.Api.Infrastructure.Hosting;
using LoginApp.Api.Infrastructure.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoginApp.Api.Tests.Infrastructure.Tls;

/// <summary>
/// End-to-end tests over real TLS: Kestrel on a loopback port with the origin certificate, the client-certificate
/// validation callback and <see cref="ClientCertificateRequirementMiddleware"/>, exactly as wired in production.
/// Only the trusted authority is swapped for a test CA, since nobody outside Cloudflare can issue a certificate
/// from the real one.
/// </summary>
public sealed class OriginTlsKestrelTests : IAsyncLifetime
{
    private readonly X509Certificate2 _trustedAuthority = TestCertificates.CreateAuthority("Test origin-pull CA");
    private readonly X509Certificate2 _otherAuthority = TestCertificates.CreateAuthority("Some other CA");
    private readonly string _serverPfxBase64 = TestCertificates.CreateServerPfxBase64();
    private WebApplication? _app;
    private Uri? _baseAddress;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        // The production host (WebApplication.CreateBuilder) enables HTTPS addresses by default; the slim one doesn't.
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.UseUrls("https://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["OriginTls:CertificatePfxBase64"] = _serverPfxBase64;
        builder.Configuration["OriginTls:RequireCloudflareClientCertificate"] = "true";

        builder.Services.AddOriginTls();
        builder.Services.AddSingleton(new ClientCertificateValidator([_trustedAuthority], TimeProvider.System));

        _app = builder.Build();
        _app.UseClientCertificateRequirement();
        _app.MapGet("/ping", () => "pong");
        _app.MapGet(HealthCheckExtensions.LivenessPath, () => "Healthy");

        await _app.StartAsync(TestContext.Current.CancellationToken);
        string address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _baseAddress = new Uri(address);
    }

    [Fact]
    public async Task Request_WithTrustedClientCertificate_IsServed()
    {
        // Arrange
        using X509Certificate2 client = TestCertificates.IssueLeaf(_trustedAuthority, "cloudflare");
        using HttpClient http = CreateClient(client);

        // Act
        using HttpResponseMessage response = await http.GetAsync(new Uri(_baseAddress!, "/ping"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithoutClientCertificate_IsForbidden()
    {
        // Arrange
        using HttpClient http = CreateClient(clientCertificate: null);

        // Act
        using HttpResponseMessage response = await http.GetAsync(new Uri(_baseAddress!, "/ping"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthProbe_WithoutClientCertificate_IsServed()
    {
        // Arrange: Container Apps' probes can't present a certificate.
        using HttpClient http = CreateClient(clientCertificate: null);

        // Act
        using HttpResponseMessage response = await http.GetAsync(new Uri(_baseAddress!, HealthCheckExtensions.LivenessPath), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithUntrustedClientCertificate_FailsTheHandshake()
    {
        // Arrange
        using X509Certificate2 client = TestCertificates.IssueLeaf(_otherAuthority, "impostor");
        using HttpClient http = CreateClient(client);

        // Act + Assert: Kestrel aborts the TLS handshake, so there is no HTTP response at all.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetAsync(new Uri(_baseAddress!, "/ping"), TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _trustedAuthority.Dispose();
        _otherAuthority.Dispose();
    }

    private HttpClient CreateClient(X509Certificate2? clientCertificate)
    {
        using X509Certificate2 server = OriginCertificates.LoadServerCertificate(_serverPfxBase64);
        string serverThumbprint = server.GetCertHashString(HashAlgorithmName.SHA256);

#pragma warning disable CA2000 // The HttpClient owns and disposes the handler.
        SocketsHttpHandler handler = new()
        {
            SslOptions =
            {
                // The test server's certificate is self-signed, so pin it instead of chaining to a public root.
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is X509Certificate2 presented
                    && presented.GetCertHashString(HashAlgorithmName.SHA256) == serverThumbprint,
                // Offer the certificate whatever issuers the server lists, as Cloudflare does.
                LocalCertificateSelectionCallback = (_, _, _, _, _) => clientCertificate!,
            },
        };
#pragma warning restore CA2000
        return new HttpClient(handler, disposeHandler: true);
    }
}
