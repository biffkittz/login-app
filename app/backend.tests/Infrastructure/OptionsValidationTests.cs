using System.ComponentModel.DataAnnotations;
using LoginApp.Api.Features.Auth;
using LoginApp.Api.Features.Auth.Notifications;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.CostEstimates;
using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Features.GroceryList.Runs;
using LoginApp.Api.Infrastructure.Email;
using LoginApp.Api.Infrastructure.Networking;
using LoginApp.Api.Infrastructure.Telemetry;
using LoginApp.Api.Infrastructure.Temporal;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Startup validation rules for configuration that must fail fast when it is missing or unsafe.
/// </summary>
public sealed class OptionsValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("c2hvcnQta2V5")] // "short-key": valid Base64, but only 9 bytes
    public void JwtOptions_WithMissingOrWeakSigningKey_FailValidation(string signingKey)
    {
        // Arrange
        JwtOptions options = new() { SigningKey = signingKey };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JwtOptions_With256BitSigningKey_PassValidation()
    {
        // Arrange
        JwtOptions options = new() { SigningKey = Convert.ToBase64String(new byte[JwtOptions.MinimumSigningKeyBytes]) };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("__Host-LoginApp.Auth", false, false)] // browsers drop __Host- cookies that are not Secure
    [InlineData("__Host-LoginApp.Auth", true, true)]
    [InlineData("LoginApp.Auth", false, true)] // local development over plain http://localhost
    public void AuthCookieOptions_HostPrefixRequiresSecure(string name, bool secure, bool expectedValid)
    {
        // Arrange
        AuthCookieOptions options = new() { Name = name, Secure = secure };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Equal(expectedValid, errors.Count == 0);
    }

    [Theory]
    [InlineData("")] // no stores at all
    [InlineData("Costco|costco")]
    [InlineData("Costco| ")]
    [InlineData(" Costco")]
    public void GroceryListOptions_WithMissingDuplicateOrBlankStores_FailValidation(string stores)
    {
        // Arrange: stores separated by "|".
        GroceryListOptions options = new() { Stores = stores.Length == 0 ? [] : [.. stores.Split('|')] };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void GroceryListOptions_FindStore_MatchesIgnoringCaseAndSpaces()
    {
        // Arrange
        GroceryListOptions options = new() { Stores = ["Whole Foods", "Teeter/Lion"] };

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.Equal("Teeter/Lion", options.FindStore(" teeter/lion "));
        Assert.Null(options.FindStore("Aldi"));
    }

    [Fact]
    public void NetworkOptions_WithMalformedRange_FailValidation()
    {
        // Arrange
        NetworkOptions options = new() { InternalNetworks = ["10.0.0.0/8", "10.0.0.0/33"] };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        ValidationResult error = Assert.Single(errors);
        Assert.Contains("10.0.0.0/33", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NetworkOptions_WithoutRanges_AreOnlyValidWhenRestrictionIsOff(bool restrict, bool expectedValid)
    {
        // Arrange
        NetworkOptions options = new() { RestrictToInternalNetworks = restrict, InternalNetworks = [] };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Equal(expectedValid, errors.Count == 0);
    }

    // Mirrors what ValidateDataAnnotations() does at startup, including IValidatableObject rules.
    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/v1/")]
    [InlineData("http://localhost:5099/v1/")]
    [InlineData("http://127.0.0.1:5099/v1/")]
    public void ImageGenerationOptions_WithHttpsOrLocalEndpoint_PassValidation(string endpoint)
    {
        // Arrange
        ImageGenerationOptions options = new() { Endpoint = new Uri(endpoint) };

        // Act + Assert
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("http://generativelanguage.googleapis.com/v1/")] // the key would cross the network unencrypted
    [InlineData("ftp://localhost/v1/")]
    public void ImageGenerationOptions_WithUnsafeEndpoint_FailValidation(string endpoint)
    {
        // Arrange
        ImageGenerationOptions options = new() { Endpoint = new Uri(endpoint) };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../tunedModels/x")] // would change the request path
    [InlineData("gemini image")]
    public void ImageGenerationOptions_WithInvalidModel_FailValidation(string model)
    {
        // Arrange
        ImageGenerationOptions options = new() { Model = model };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void ImageGenerationOptions_Defaults_PassValidationButDrawNothingWithoutAKey()
    {
        // Arrange
        ImageGenerationOptions options = new();

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.False(options.IsConfigured);
        Assert.Equal("gemini-3.1-flash-lite-image", options.Model);
    }

    [Fact]
    public void CostEstimateOptions_Defaults_PassValidation_AndUseFlashLite500TimesADay()
    {
        // Arrange
        CostEstimateOptions options = new();

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.Equal("gemini-3.1-flash-lite", options.Model);
        Assert.Equal(500, options.MaxEstimatesPerDay);
        Assert.False(options.IsAvailable(new ImageGenerationOptions()));
        Assert.True(options.IsAvailable(new ImageGenerationOptions { ApiKey = "key" }));
        Assert.False(new CostEstimateOptions { MaxEstimatesPerDay = 0 }.IsAvailable(new ImageGenerationOptions { ApiKey = "key" }));
    }

    [Theory]
    [InlineData("http://generativelanguage.googleapis.com/v1beta/")] // the key would cross the network unencrypted
    [InlineData("ftp://localhost/v1beta/")]
    public void CostEstimateOptions_WithUnsafeEndpoint_FailValidation(string endpoint)
    {
        // Arrange
        CostEstimateOptions options = new() { Endpoint = new Uri(endpoint) };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../tunedModels/x")] // would change the request path
    [InlineData("gemini flash")]
    public void CostEstimateOptions_WithInvalidModel_FailValidation(string model)
    {
        // Arrange
        CostEstimateOptions options = new() { Model = model };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void CostEstimateOptions_WithDailyLimitOutOfRange_FailValidation(int limit)
    {
        // Arrange
        CostEstimateOptions options = new() { MaxEstimatesPerDay = limit };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void EmailOptions_Defaults_PassValidationButSendNothing()
    {
        // Arrange
        EmailOptions options = new();

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void EmailOptions_WithAccountTokenAndSender_PassValidation()
    {
        // Arrange
        EmailOptions options = ConfiguredEmail();

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData("", "token", "notifications@example.com")] // the token's Key Vault secret is there, the rest isn't
    [InlineData("0123456789abcdef0123456789abcdef", "", "notifications@example.com")] // the secret is missing
    [InlineData("0123456789abcdef0123456789abcdef", "token", "")]
    public void EmailOptions_WithHalfAConfiguration_FailValidation(string accountId, string apiToken, string senderAddress)
    {
        // Arrange
        EmailOptions options = new() { AccountId = accountId, ApiToken = apiToken, SenderAddress = senderAddress };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")] // Cloudflare IDs are lowercase
    [InlineData("0123456789abcdef")]
    [InlineData("../zones/0123456789abcdef0123456789")]
    public void EmailOptions_WithMalformedAccountId_FailValidation(string accountId)
    {
        // Arrange
        EmailOptions options = ConfiguredEmail();
        options.AccountId = accountId;

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("Nibbles <notifications@example.com>")]
    [InlineData("notifications")]
    [InlineData("notifications@localhost")]
    [InlineData("notifications@example.com\r\nBcc: someone@example.org")]
    public void EmailOptions_WithAnythingButAPlainSenderAddress_FailValidation(string senderAddress)
    {
        // Arrange
        EmailOptions options = ConfiguredEmail();
        options.SenderAddress = senderAddress;

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("http://api.cloudflare.com/client/v4/")]
    [InlineData("ftp://api.cloudflare.com/")]
    public void EmailOptions_WithUnsafeEndpoint_FailValidation(string endpoint)
    {
        // Arrange
        EmailOptions options = ConfiguredEmail();
        options.Endpoint = new Uri(endpoint);

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void EmailOptions_WithAControlCharacterInTheSenderName_FailValidation()
    {
        // Arrange: a line break in the From header could start a header of its own.
        EmailOptions options = ConfiguredEmail();
        options.SenderName = "Nibbles\r\nBcc: someone@example.org";

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void RegistrationNotificationOptions_Defaults_PassValidationAndEmailNobody()
    {
        // Arrange
        RegistrationNotificationOptions options = new();

        // Act + Assert
        Assert.Empty(Validate(options));
        Assert.Empty(options.Recipients);
        Assert.Equal("[grocery app] new registration detected", options.Subject);
    }

    [Theory]
    [InlineData("owner@example.com")]
    [InlineData("owner@example.com|second.person+family@example.co.uk")]
    public void RegistrationNotificationOptions_WithPlainDistinctRecipients_PassValidation(string recipients)
    {
        // Arrange: recipients separated by "|".
        RegistrationNotificationOptions options = new()
        {
            Recipients = [.. recipients.Split('|')],
            SiteUrl = new Uri("https://grocery.example.com"),
        };

        // Act + Assert
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("owner@example.com|OWNER@example.com")] // twice, in different case
    [InlineData("Owner <owner@example.com>")]
    [InlineData("owner")]
    [InlineData(" owner@example.com")]
    public void RegistrationNotificationOptions_WithRepeatedOrMalformedRecipients_FailValidation(string recipients)
    {
        // Arrange
        RegistrationNotificationOptions options = new() { Recipients = [.. recipients.Split('|')] };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void RegistrationNotificationOptions_WithMoreRecipientsThanOneEmailCanHave_FailValidation()
    {
        // Arrange
        RegistrationNotificationOptions options = new()
        {
            Recipients = [.. Enumerable.Range(0, CloudflareEmailSender.MaxRecipients + 1).Select(index => $"person{index}@example.com")],
        };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("[grocery app] new registration\r\nBcc: someone@example.org")]
    [InlineData("")]
    public void RegistrationNotificationOptions_WithAnUnsafeOrEmptySubject_FailValidation(string subject)
    {
        // Arrange
        RegistrationNotificationOptions options = new() { Subject = subject };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("ftp://grocery.example.com")]
    public void RegistrationNotificationOptions_WithASiteUrlThatIsNotAWebAddress_FailValidation(string siteUrl)
    {
        // Arrange
        RegistrationNotificationOptions options = new() { SiteUrl = new Uri(siteUrl, UriKind.RelativeOrAbsolute) };

        // Act + Assert
        Assert.NotEmpty(Validate(options));
    }

    private static EmailOptions ConfiguredEmail() =>
        new()
        {
            AccountId = "0123456789abcdef0123456789abcdef",
            ApiToken = "test-email-token",
            SenderAddress = "notifications@example.com",
        };

    [Fact]
    public void TelemetryOptions_Defaults_PassValidationButExportNothing()
    {
        // Arrange
        TelemetryOptions options = new();

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
        Assert.False(options.IsExporting);
    }

    [Theory]
    [InlineData("https://prometheus.internal.example.azurecontainerapps.io/api/v1/otlp/v1/metrics")]
    [InlineData("http://localhost:9090/api/v1/otlp/v1/metrics")]
    [InlineData("http://127.0.0.1:9090/api/v1/otlp/v1/metrics")]
    public void TelemetryOptions_WithAnHttpsOrLocalEndpointAndCredentials_PassValidation(string endpoint)
    {
        // Arrange
        TelemetryOptions options = new()
        {
            OtlpEndpoint = new Uri(endpoint),
            OtlpUsername = "app",
            OtlpPassword = "a-long-random-password",
            Site = "app.biffkittz.com",
        };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
        Assert.True(options.IsExporting);
    }

    [Theory]
    [InlineData("http://prometheus.internal.example.azurecontainerapps.io/api/v1/otlp/v1/metrics")] // password in the clear
    [InlineData("ftp://localhost/api/v1/otlp/v1/metrics")]
    public void TelemetryOptions_WithAnEndpointThatIsNotHttps_FailValidation(string endpoint)
    {
        // Arrange
        TelemetryOptions options = new() { OtlpEndpoint = new Uri(endpoint), OtlpUsername = "app", OtlpPassword = "secret" };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TelemetryOptions.OtlpEndpoint)));
    }

    [Theory]
    [InlineData("https://prometheus.example/api/v1/otlp/v1/metrics", "app", null)]
    [InlineData("https://prometheus.example/api/v1/otlp/v1/metrics", null, "secret")]
    [InlineData(null, "app", "secret")] // credentials for nowhere
    public void TelemetryOptions_WithHalfTheCredentials_FailValidation(string? endpoint, string? username, string? password)
    {
        // Arrange
        TelemetryOptions options = new()
        {
            OtlpEndpoint = endpoint is null ? null : new Uri(endpoint),
            OtlpUsername = username,
            OtlpPassword = password,
        };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TelemetryOptions.OtlpUsername)));
    }

    [Theory]
    [InlineData("app:admin")] // a colon would split "user:password" in the Basic credentials
    [InlineData("app\r\nX-Injected: 1")]
    public void TelemetryOptions_WithAUserNameThatBreaksBasicAuthentication_FailValidation(string username)
    {
        // Arrange
        TelemetryOptions options = new()
        {
            OtlpEndpoint = new Uri("https://prometheus.example/api/v1/otlp/v1/metrics"),
            OtlpUsername = username,
            OtlpPassword = "secret",
        };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TelemetryOptions.OtlpUsername)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TelemetryOptions.MaxExportIntervalSeconds + 1)]
    public void TelemetryOptions_WithAnExportIntervalOutOfRange_FailValidation(int seconds)
    {
        // Arrange
        TelemetryOptions options = new() { ExportIntervalSeconds = seconds };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TelemetryOptions.ExportIntervalSeconds)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("app.biffkittz.com\n")]
    public void TelemetryOptions_WithAnEmptyOrMultilineSite_FailValidation(string site)
    {
        // Arrange
        TelemetryOptions options = new() { Site = site };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(TelemetryOptions.Site)));
    }

    [Fact]
    public void TemporalOptions_Defaults_PassValidationWithoutTemporal()
    {
        // Arrange
        TemporalOptions options = new();

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
        Assert.False(options.IsConfigured);
    }

    [Theory]
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud:7233", "nibbles-demo.a1b2c", "an-api-key")]
    [InlineData("localhost:7233", "default", null)] // the development server: no key, no TLS
    [InlineData("127.0.0.1:7233", "default", null)]
    [InlineData("[::1]:7233", "default", null)]
    public void TemporalOptions_WithCloudOrLocalSettings_PassValidation(string address, string @namespace, string? apiKey)
    {
        // Arrange
        TemporalOptions options = new() { Address = address, Namespace = @namespace, ApiKey = apiKey };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud:7233", "nibbles-demo.a1b2c", null)] // plain text across the network
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud", "nibbles-demo.a1b2c", "an-api-key")] // no port
    [InlineData("https://nibbles-demo.a1b2c.tmprl.cloud:7233", "nibbles-demo.a1b2c", "an-api-key")] // a URL
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud:99999", "nibbles-demo.a1b2c", "an-api-key")]
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud:7233", null, "an-api-key")] // no namespace
    [InlineData("nibbles-demo.a1b2c.tmprl.cloud:7233", "nibbles-demo.a1b2c", "an api key")] // pasted with a space
    [InlineData(null, "nibbles-demo.a1b2c", "an-api-key")] // half a configuration
    public void TemporalOptions_WithUnsafeOrIncompleteSettings_FailValidation(string? address, string? @namespace, string? apiKey)
    {
        // Arrange
        TemporalOptions options = new() { Address = address, Namespace = @namespace, ApiKey = apiKey };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nibbles main")]
    [InlineData("-nibbles")]
    public void TemporalOptions_WithAnInvalidTaskQueue_FailValidation(string taskQueue)
    {
        // Arrange
        TemporalOptions options = new() { TaskQueue = taskQueue };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void GroceryRunOptions_WithTheShippedChoices_PassValidation()
    {
        // Arrange
        GroceryRunOptions options = new() { LastCallMinutes = [15, 30, 60, 120] };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(new int[0], 240)] // nothing to choose from
    [InlineData(new[] { 15, 15 }, 240)] // offered twice
    [InlineData(new[] { 0 }, 240)]
    [InlineData(new[] { 241 }, 240)] // longer than 4 hours
    [InlineData(new[] { 15 }, 5)] // a shopping limit too short to shop in
    public void GroceryRunOptions_WithInvalidChoices_FailValidation(int[] lastCallMinutes, int shoppingTimeLimitMinutes)
    {
        // Arrange
        GroceryRunOptions options = new() { LastCallMinutes = [.. lastCallMinutes], ShoppingTimeLimitMinutes = shoppingTimeLimitMinutes };

        // Act
        List<ValidationResult> errors = Validate(options);

        // Assert
        Assert.NotEmpty(errors);
    }

    private static List<ValidationResult> Validate(object options)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
