using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// Decides whether a TLS client certificate chains to one of a fixed set of authorities.
/// </summary>
/// <remarks>
/// The operating system's trusted roots play no part: only the given authorities count, so a certificate from any
/// public CA is refused. Revocation isn't checked, because Cloudflare publishes no revocation list for its
/// origin-pull certificates.
/// </remarks>
internal sealed class ClientCertificateValidator
{
    // id-kp-clientAuth (RFC 5280, section 4.2.1.12). A certificate without an extended key usage extension is
    // valid for any purpose, so this rules out only certificates issued for something else.
    private static readonly Oid s_clientAuthentication = new("1.3.6.1.5.5.7.3.2");

    private readonly X509Certificate2Collection _trustedAuthorities;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientCertificateValidator"/> class.
    /// </summary>
    /// <param name="trustedAuthorities">The only roots a client certificate may chain to.</param>
    /// <param name="timeProvider">The clock that validity periods are checked against.</param>
    public ClientCertificateValidator(X509Certificate2Collection trustedAuthorities, TimeProvider timeProvider)
    {
        _trustedAuthorities = trustedAuthorities;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Builds a chain from <paramref name="certificate"/> to one of the trusted authorities.
    /// </summary>
    /// <param name="certificate">The client's certificate.</param>
    /// <param name="presentedChain">The chain TLS built from what the client sent, if any. Its intermediates may
    /// help build the chain; its roots are never trusted for that reason alone.</param>
    /// <returns>True if the certificate is currently valid, meant for client authentication and issued (directly or
    /// through intermediates) by a trusted authority.</returns>
    public bool IsTrusted(X509Certificate2 certificate, X509Chain? presentedChain = null)
    {
        using X509Chain chain = new();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(_trustedAuthorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(s_clientAuthentication);
        chain.ChainPolicy.VerificationTime = _timeProvider.GetUtcNow().UtcDateTime;

        if (presentedChain is not null)
        {
            foreach (X509ChainElement element in presentedChain.ChainElements)
            {
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        try
        {
            return chain.Build(certificate);
        }
        finally
        {
            // Chain elements are new certificate instances that each hold a native handle.
            foreach (X509ChainElement element in chain.ChainElements)
            {
                element.Certificate.Dispose();
            }
        }
    }
}
