using System.ComponentModel.DataAnnotations;
using LoginApp.Api.Features.Auth;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Infrastructure.Networking;

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
    private static List<ValidationResult> Validate(object options)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
