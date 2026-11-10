using System.ComponentModel.DataAnnotations;
using System.Net;
using LoginApp.Api.Features.GroceryList.Images;

namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// How the app estimates what the grocery list costs with the Gemini API, bound from the <c>CostEstimates</c>
/// configuration section.
/// </summary>
/// <remarks>
/// There is no API key here: estimates use the app's one Gemini key, <c>ImageGeneration:ApiKey</c>, which Terraform
/// already delivers through Key Vault. Without it there are no estimates, as there are no pictures.
/// </remarks>
internal sealed class CostEstimateOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "CostEstimates";

    /// <summary>
    /// Gets or sets the text model. Gemini 3.1 Flash-Lite, Google's cheapest and fastest, is plenty for pricing a
    /// shopping list, and supports the structured (JSON Schema) answers the app relies on.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9.-]{0,99}$", ErrorMessage = "CostEstimates:Model must be a model code such as gemini-3.1-flash-lite.")]
    public string Model { get; set; } = "gemini-3.1-flash-lite";

    /// <summary>
    /// Gets or sets the base URL of the Gemini API; requests go to <c>models/{Model}:generateContent</c> under it.
    /// v1beta, because that is where Google documents <c>responseJsonSchema</c>, which holds the model to the schema.
    /// </summary>
    [Required]
    public Uri Endpoint { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>
    /// Gets or sets how many estimates may be asked for in any 24 hours, by everyone together. Anyone can register,
    /// and every tap of the button is a paid request, so this caps what a runaway or hostile account can spend. A tap
    /// over the limit is refused (429). 0 turns estimates off.
    /// </summary>
    [Range(0, 100_000)]
    public int MaxEstimatesPerDay { get; set; } = 500;

    /// <summary>
    /// Tells whether the server can estimate at all: it has a Gemini API key, and estimates aren't turned off.
    /// </summary>
    /// <param name="gemini">The Gemini settings, which hold the API key.</param>
    /// <returns><see langword="true"/> if the Estimate Cost button can work.</returns>
    public bool IsAvailable(ImageGenerationOptions gemini) => gemini.IsConfigured && MaxEstimatesPerDay > 0;

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
                "CostEstimates:Endpoint must be an absolute https:// URL (http:// only for localhost).",
                [nameof(Endpoint)]);
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
