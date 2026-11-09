using System.Security.Cryptography.X509Certificates;
using LoginApp.Api.Infrastructure.Tls;
using Microsoft.Extensions.Time.Testing;

namespace LoginApp.Api.Tests.Infrastructure.Tls;

/// <summary>
/// Unit tests for <see cref="ClientCertificateValidator"/>.
/// </summary>
public sealed class ClientCertificateValidatorTests : IDisposable
{
    private readonly X509Certificate2 _trustedAuthority = TestCertificates.CreateAuthority("Trusted test CA");
    private readonly X509Certificate2 _otherAuthority = TestCertificates.CreateAuthority("Other test CA");

    [Fact]
    public void IsTrusted_ClientCertificateFromTrustedAuthority_IsTrusted()
    {
        // Arrange
        using X509Certificate2 client = TestCertificates.IssueLeaf(_trustedAuthority, "client");

        // Act
        bool trusted = CreateValidator().IsTrusted(client);

        // Assert
        Assert.True(trusted);
    }

    [Fact]
    public void IsTrusted_ClientCertificateFromAnotherAuthority_IsRefused()
    {
        // Arrange
        using X509Certificate2 client = TestCertificates.IssueLeaf(_otherAuthority, "client");

        // Act
        bool trusted = CreateValidator().IsTrusted(client);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsTrusted_SelfSignedCertificate_IsRefused()
    {
        // Arrange: a self-signed certificate presented as its own chain must not vouch for itself.
        using X509Certificate2 impostor = TestCertificates.CreateAuthority("Trusted test CA");
        using X509Chain presented = BuildChain(impostor, impostor);

        // Act
        bool trusted = CreateValidator().IsTrusted(impostor, presented);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsTrusted_CertificateIssuedForServerAuthenticationOnly_IsRefused()
    {
        // Arrange
        using X509Certificate2 server = TestCertificates.IssueLeaf(_trustedAuthority, "server", clientAuthentication: false);

        // Act
        bool trusted = CreateValidator().IsTrusted(server);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsTrusted_AfterTheCertificateExpires_IsRefused()
    {
        // Arrange
        using X509Certificate2 client = TestCertificates.IssueLeaf(_trustedAuthority, "client");
        FakeTimeProvider clock = new(client.NotAfter.ToUniversalTime().AddDays(1));

        // Act
        bool trusted = CreateValidator(clock).IsTrusted(client);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsTrusted_WithIntermediate_NeedsThePresentedChain()
    {
        // Arrange
        using X509Certificate2 intermediate = TestCertificates.IssueIntermediate(_trustedAuthority, "intermediate");
        using X509Certificate2 client = TestCertificates.IssueLeaf(intermediate, "client");
        using X509Chain presented = BuildChain(client, intermediate);
        ClientCertificateValidator validator = CreateValidator();

        // Act
        bool trustedAlone = validator.IsTrusted(client);
        bool trustedWithChain = validator.IsTrusted(client, presented);

        // Assert
        Assert.False(trustedAlone);
        Assert.True(trustedWithChain);
    }

    [Fact]
    public void CloudflareOriginPullAuthority_LoadsFromTheEmbeddedResource()
    {
        // Act
        using X509Certificate2 authority = OriginCertificates.LoadCloudflareOriginPullAuthority();

        // Assert
        Assert.Contains("CN=origin-pull.cloudflare.net", authority.Subject, StringComparison.Ordinal);
        Assert.Equal(authority.Subject, authority.Issuer);
        Assert.Equal("9A1AC2B4BE15F9F27EEE20A734CBA4E9898F61001B3BD7C84B69B56A3E25A2B9", authority.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
    }

    public void Dispose()
    {
        _trustedAuthority.Dispose();
        _otherAuthority.Dispose();
    }

    private ClientCertificateValidator CreateValidator(TimeProvider? clock = null) =>
        new([_trustedAuthority], clock ?? TimeProvider.System);

    // Mimics the chain TLS hands to the validation callback: whatever the client sent, whether or not it's trusted.
    private static X509Chain BuildChain(X509Certificate2 leaf, X509Certificate2 extra)
    {
        X509Chain chain = new();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ExtraStore.Add(extra);
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.Build(leaf);
        return chain;
    }
}
