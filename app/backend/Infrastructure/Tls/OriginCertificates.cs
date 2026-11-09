using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// Loads the certificates used for origin TLS.
/// </summary>
internal static class OriginCertificates
{
    // Embedded rather than read from disk, so the trust anchor can't be swapped without rebuilding the image.
    private const string CloudflareOriginPullAuthorityResource = "LoginApp.Api.Infrastructure.Tls.cloudflare-origin-pull-ca.pem";

    /// <summary>
    /// Loads the server certificate and its private key from a Base64-encoded, passwordless PKCS#12 file.
    /// </summary>
    /// <param name="pfxBase64">The Base64-encoded PFX.</param>
    /// <returns>The certificate, with its private key.</returns>
    /// <exception cref="FormatException">The value is not Base64.</exception>
    /// <exception cref="CryptographicException">The value is not a passwordless PKCS#12 file.</exception>
    public static X509Certificate2 LoadServerCertificate(string pfxBase64) =>
        X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(pfxBase64.Trim()), password: null);

    /// <summary>
    /// Checks that a server certificate can be loaded and used at <paramref name="now"/>.
    /// </summary>
    /// <param name="pfxBase64">The Base64-encoded PFX.</param>
    /// <param name="now">The current time.</param>
    /// <returns>A description of the problem, completing the sentence "The certificate ...", or null if the
    /// certificate is usable. It never includes key material.</returns>
    public static string? FindServerCertificateProblem(string pfxBase64, DateTimeOffset now)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = LoadServerCertificate(pfxBase64);
        }
        catch (FormatException)
        {
            return "is not valid Base64.";
        }
        catch (CryptographicException)
        {
            return "is not a passwordless PKCS#12 (PFX) file.";
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
            {
                return $"({certificate.Subject}) has no private key.";
            }

            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                return $"({certificate.Subject}) is valid only from {certificate.NotBefore:u} to {certificate.NotAfter:u}.";
            }

            return null;
        }
    }

    /// <summary>
    /// Loads the CA that signs the client certificate Cloudflare presents to origins when Authenticated Origin
    /// Pulls is on (https://developers.cloudflare.com/ssl/static/authenticated_origin_pull_ca.pem).
    /// </summary>
    /// <returns>The CA certificate. It expires on 2029-11-01; Cloudflare will publish a replacement before then.</returns>
    public static X509Certificate2 LoadCloudflareOriginPullAuthority()
    {
        using Stream stream = typeof(OriginCertificates).Assembly.GetManifestResourceStream(CloudflareOriginPullAuthorityResource)
            ?? throw new InvalidOperationException($"The embedded resource {CloudflareOriginPullAuthorityResource} is missing.");
        using StreamReader reader = new(stream);
        return X509Certificate2.CreateFromPem(reader.ReadToEnd());
    }
}
