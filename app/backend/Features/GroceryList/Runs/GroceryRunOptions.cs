using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The choices and limits of grocery runs, bound from the <c>GroceryRuns</c> configuration section.
/// </summary>
internal sealed class GroceryRunOptions : IValidatableObject
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "GroceryRuns";

    /// <summary>The longest last call that may be offered: 4 hours.</summary>
    public const int MaxLastCallMinutes = 240;

    /// <summary>
    /// Gets or sets the lengths of last call the shopper can choose from, in minutes, in the order offered. The server
    /// accepts no others.
    /// </summary>
    /// <remarks>
    /// Set in appsettings.json rather than defaulted here: the configuration binder adds configured entries to a list's
    /// existing items instead of replacing them, so a default would end up duplicated.
    /// </remarks>
    [MinLength(1, ErrorMessage = "GroceryRuns:LastCallMinutes must offer at least one length.")]
    public List<int> LastCallMinutes { get; set; } = [];

    /// <summary>
    /// Gets or sets how long the shopping may last, in minutes, before the run ends by itself: for a shopper who forgets
    /// to say they're done.
    /// </summary>
    [Range(10, 24 * 60, ErrorMessage = "GroceryRuns:ShoppingTimeLimitMinutes must be between {1} and {2}.")]
    public int ShoppingTimeLimitMinutes { get; set; } = 240;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LastCallMinutes.Exists(minutes => minutes is < 1 or > MaxLastCallMinutes))
        {
            yield return new ValidationResult(
                $"GroceryRuns:LastCallMinutes entries must be between 1 and {MaxLastCallMinutes}.",
                [nameof(LastCallMinutes)]);
        }

        if (LastCallMinutes.Distinct().Count() != LastCallMinutes.Count)
        {
            yield return new ValidationResult("GroceryRuns:LastCallMinutes must not offer a length twice.", [nameof(LastCallMinutes)]);
        }
    }
}
