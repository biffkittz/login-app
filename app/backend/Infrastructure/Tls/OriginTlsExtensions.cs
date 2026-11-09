using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// Registers HTTPS termination in Kestrel and the Cloudflare client-certificate requirement.
/// </summary>
internal static class OriginTlsExtensions
{
    /// <summary>
    /// Adds validated <see cref="OriginTlsOptions"/> and, when a certificate is configured, makes it Kestrel's
    /// default HTTPS certificate.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOriginTls(this IServiceCollection services)
    {
        services.AddOptions<OriginTlsOptions>()
            .BindConfiguration(OriginTlsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(static provider => new ClientCertificateValidator(
            [OriginCertificates.LoadCloudflareOriginPullAuthority()],
            provider.GetRequiredService<TimeProvider>()));

        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<OriginTlsOptions>, ClientCertificateValidator, ILoggerFactory>(ConfigureKestrel);

        return services;
    }

    /// <summary>
    /// Adds <see cref="ClientCertificateRequirementMiddleware"/> when
    /// <see cref="OriginTlsOptions.RequireCloudflareClientCertificate"/> is true.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder, for chaining.</returns>
    public static IApplicationBuilder UseClientCertificateRequirement(this IApplicationBuilder app)
    {
        OriginTlsOptions options = app.ApplicationServices.GetRequiredService<IOptions<OriginTlsOptions>>().Value;
        return options.RequireCloudflareClientCertificate
            ? app.UseMiddleware<ClientCertificateRequirementMiddleware>()
            : app;
    }

    private static void ConfigureKestrel(
        KestrelServerOptions kestrel,
        IOptions<OriginTlsOptions> options,
        ClientCertificateValidator validator,
        ILoggerFactory loggerFactory)
    {
        // Reading Value runs the options validation, so a broken certificate stops startup with a clear message.
        OriginTlsOptions tls = options.Value;
        if (!tls.HasCertificate)
        {
            return;
        }

        // Kept for the lifetime of the process: Kestrel uses it for every handshake.
#pragma warning disable CA2000 // Dispose objects before losing scope
        X509Certificate2 certificate = OriginCertificates.LoadServerCertificate(tls.CertificatePfxBase64!);
#pragma warning restore CA2000
        ILogger logger = loggerFactory.CreateLogger(typeof(OriginTlsExtensions));
        DateTime expiresUtc = certificate.NotAfter.ToUniversalTime();
        OriginTlsLog.ServingHttps(
            logger, certificate.Subject, certificate.Thumbprint, expiresUtc, tls.RequireCloudflareClientCertificate);

        kestrel.ConfigureHttpsDefaults(https =>
        {
            https.ServerCertificate = certificate;
            https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            if (tls.RequireCloudflareClientCertificate)
            {
                // Allow, not Require: the platform's health probes connect without a certificate.
                // ClientCertificateRequirementMiddleware turns the missing certificate into a 403 everywhere else.
                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                https.ClientCertificateValidation = (clientCertificate, presentedChain, _) =>
                {
                    // The policy errors from the operating system's trust store are ignored on purpose: the
                    // Cloudflare origin-pull CA isn't a public root, so the validator decides on its own.
                    if (validator.IsTrusted(clientCertificate, presentedChain))
                    {
                        return true;
                    }

                    OriginTlsLog.ClientCertificateRejected(logger, clientCertificate.Issuer);
                    return false;
                };
            }
        });
    }
}
