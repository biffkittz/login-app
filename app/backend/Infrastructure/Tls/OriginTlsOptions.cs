using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// HTTPS served by Kestrel itself, bound from the <c>OriginTls</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// In Azure, Cloudflare connects straight to the container: Container Apps passes the TCP connection through on
/// port 443 without decrypting it, so Kestrel terminates TLS with the Cloudflare origin certificate. Locally
/// neither setting is present and the app serves plain HTTP as before.
/// </para>
/// <para>
/// Kestrel only applies the certificate to HTTPS endpoints; which endpoints exist is still decided by
/// <c>ASPNETCORE_URLS</c> / <c>ASPNETCORE_HTTPS_PORTS</c>.
/// </para>
/// </remarks>
internal sealed class OriginTlsOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "OriginTls";

    /// <summary>
    /// Gets or sets the server certificate and its private key as a Base64-encoded PKCS#12 (PFX) file without a
    /// password. That is the format Key Vault returns from a certificate's secret, which Container Apps passes in
    /// as <c>OriginTls__CertificatePfxBase64</c>.
    /// </summary>
    public string? CertificatePfxBase64 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every request except the health probes must arrive on a connection
    /// that presented Cloudflare's Authenticated Origin Pulls client certificate.
    /// </summary>
    /// <remarks>
    /// This is what restricts the publicly reachable container to Cloudflare: the network can't do it, because
    /// a public Container Apps environment doesn't route inbound traffic through its subnet's NSG.
    /// </remarks>
    public bool RequireCloudflareClientCertificate { get; set; }

    /// <summary>Gets a value indicating whether a server certificate is configured.</summary>
    public bool HasCertificate => !string.IsNullOrWhiteSpace(CertificatePfxBase64);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequireCloudflareClientCertificate && !HasCertificate)
        {
            yield return new ValidationResult(
                "OriginTls:RequireCloudflareClientCertificate needs OriginTls:CertificatePfxBase64: client certificates only exist on HTTPS connections.",
                [nameof(RequireCloudflareClientCertificate)]);
        }

        if (HasCertificate
            && OriginCertificates.FindServerCertificateProblem(CertificatePfxBase64!, DateTimeOffset.UtcNow) is { } problem)
        {
            yield return new ValidationResult($"OriginTls:CertificatePfxBase64 {problem}", [nameof(CertificatePfxBase64)]);
        }
    }
}
