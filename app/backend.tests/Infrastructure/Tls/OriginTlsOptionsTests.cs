using System.ComponentModel.DataAnnotations;
using LoginApp.Api.Infrastructure.Tls;

namespace LoginApp.Api.Tests.Infrastructure.Tls;

/// <summary>
/// Startup validation of <see cref="OriginTlsOptions"/>: a certificate that can't be served must stop the app.
/// </summary>
public sealed class OriginTlsOptionsTests
{
    [Fact]
    public void WithoutCertificate_LocalDevelopmentDefaults_PassValidation()
    {
        // Arrange
        OriginTlsOptions options = new();

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void RequiringClientCertificatesWithoutServerCertificate_FailsValidation()
    {
        // Arrange
        OriginTlsOptions options = new() { RequireCloudflareClientCertificate = true };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        ValidationResult error = Assert.Single(errors);
        Assert.Contains("CertificatePfxBase64", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WithUsableCertificate_PassesValidation()
    {
        // Arrange
        OriginTlsOptions options = new()
        {
            CertificatePfxBase64 = TestCertificates.CreateServerPfxBase64(),
            RequireCloudflareClientCertificate = true,
        };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("not base64 at all!", "Base64")]
    [InlineData("bm90IGEgcGZ4", "PKCS#12")] // "not a pfx": valid Base64, but not a certificate
    public void WithUnreadableCertificate_FailsValidation(string value, string expectedReason)
    {
        // Arrange
        OriginTlsOptions options = new() { CertificatePfxBase64 = value };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        ValidationResult error = Assert.Single(errors);
        Assert.Contains(expectedReason, error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WithCertificateWithoutPrivateKey_FailsValidation()
    {
        // Arrange
        OriginTlsOptions options = new() { CertificatePfxBase64 = TestCertificates.CreateServerPfxBase64(includePrivateKey: false) };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        ValidationResult error = Assert.Single(errors);
        Assert.Contains("private key", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WithExpiredCertificate_FailsValidation()
    {
        // Arrange
        string expired = TestCertificates.CreateServerPfxBase64(
            notBefore: DateTimeOffset.UtcNow.AddDays(-30), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        OriginTlsOptions options = new() { CertificatePfxBase64 = expired };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        ValidationResult error = Assert.Single(errors);
        Assert.Contains("valid only from", error.ErrorMessage, StringComparison.Ordinal);
    }

    // Mirrors what ValidateDataAnnotations() does at startup, including IValidatableObject rules.
    private static List<ValidationResult> Validate(OriginTlsOptions options)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
