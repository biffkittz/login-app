using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LoginApp.Api.Tests.Infrastructure.Tls;

/// <summary>
/// Creates throwaway certificates for the TLS tests. ECDSA P-256 keeps key generation fast.
/// </summary>
internal static class TestCertificates
{
    private static readonly Oid s_clientAuthentication = new("1.3.6.1.5.5.7.3.2");
    private static readonly Oid s_serverAuthentication = new("1.3.6.1.5.5.7.3.1");

    /// <summary>Creates a self-signed certificate authority.</summary>
    public static X509Certificate2 CreateAuthority(string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(5));
    }

    /// <summary>Issues a client (or, with <paramref name="clientAuthentication"/> false, server) certificate.</summary>
    public static X509Certificate2 IssueLeaf(
        X509Certificate2 issuer,
        string name,
        bool clientAuthentication = true,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null) =>
        Issue(issuer, name, certificateAuthority: false, clientAuthentication ? s_clientAuthentication : s_serverAuthentication, notBefore, notAfter);

    /// <summary>Issues an intermediate certificate authority.</summary>
    public static X509Certificate2 IssueIntermediate(X509Certificate2 issuer, string name) =>
        Issue(issuer, name, certificateAuthority: true, usage: null, notBefore: null, notAfter: null);

    /// <summary>Creates a self-signed server certificate and returns it as a Base64, passwordless PFX.</summary>
    public static string CreateServerPfxBase64(DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, bool includePrivateKey = true)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=localhost", key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([s_serverAuthentication], critical: false));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30));

        if (includePrivateKey)
        {
            return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
        }

        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return Convert.ToBase64String(publicOnly.Export(X509ContentType.Pkcs12));
    }

    private static X509Certificate2 Issue(
        X509Certificate2 issuer,
        string name,
        bool certificateAuthority,
        Oid? usage,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        if (certificateAuthority)
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        }

        if (usage is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([usage], critical: false));
        }

        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] &= 0x7F; // positive
        using X509Certificate2 publicOnly = request.Create(
            issuer,
            notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
            notAfter ?? DateTimeOffset.UtcNow.AddYears(1),
            serialNumber);
        return publicOnly.CopyWithPrivateKey(key);
    }
}
