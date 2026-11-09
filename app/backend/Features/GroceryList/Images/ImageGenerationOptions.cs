using System.ComponentModel.DataAnnotations;
using System.Net;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// How the app draws pictures of grocery items with the Gemini API, bound from the <c>ImageGeneration</c>
/// configuration section.
/// </summary>
internal sealed class ImageGenerationOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "ImageGeneration";

    /// <summary>
    /// Gets or sets the Gemini API key. A secret: Key Vault (<c>ImageGeneration--ApiKey</c>) in Azure, user secrets
    /// on a developer machine. Without one, the app draws no pictures and the in-app switch says so.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the image model. The default is Google's cheapest and fastest image model: a 40-pixel picture in
    /// a list doesn't need a better one, and a picture costs about three cents.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9.-]{0,99}$", ErrorMessage = "ImageGeneration:Model must be a model code such as gemini-3.1-flash-lite-image.")]
    public string Model { get; set; } = "gemini-3.1-flash-lite-image";

    /// <summary>Gets or sets the base URL of the Gemini API; requests go to <c>models/{Model}:generateContent</c> under it.</summary>
    [Required]
    public Uri Endpoint { get; set; } = new("https://generativelanguage.googleapis.com/v1/");

    /// <summary>
    /// Gets or sets how many pictures may be drawn in any 24 hours. Anyone can register, and every new item name
    /// costs a picture, so this caps what a runaway or hostile account can spend. Items added over the limit simply
    /// get no picture. 0 stops drawing altogether.
    /// </summary>
    [Range(0, 10_000)]
    public int MaxImagesPerDay { get; set; } = 100;

    /// <summary>Gets a value indicating whether an API key is configured, without which no picture is drawn.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Plain HTTP only reaches a stand-in for Gemini on this machine (tests). Anything else would send the API key
        // across the network unencrypted.
        bool isHttps = Endpoint.IsAbsoluteUri && Endpoint.Scheme == Uri.UriSchemeHttps;
        bool isLocalHttp = Endpoint.IsAbsoluteUri && Endpoint.Scheme == Uri.UriSchemeHttp && IsLoopback(Endpoint);
        if (!isHttps && !isLocalHttp)
        {
            yield return new ValidationResult(
                "ImageGeneration:Endpoint must be an absolute https:// URL (http:// only for localhost).",
                [nameof(Endpoint)]);
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
