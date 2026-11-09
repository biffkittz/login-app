using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// Rejects text containing control characters, which have no business in a grocery list and can disturb how other
/// users' screens lay it out. <see langword="null"/> is valid; combine with <see cref="RequiredAttribute"/> as needed.
/// </summary>
/// <remarks>
/// Only the C0 and C1 control characters (Unicode category Cc) are refused. Format characters (Cf) such as the
/// zero-width joiner stay allowed, because emoji like 👨‍👩‍👧 depend on them.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
internal sealed class PlainTextAttribute : ValidationAttribute
{
    /// <summary>Gets or sets a value indicating whether line breaks (and tabs) are allowed, as in notes.</summary>
    public bool AllowLineBreaks { get; set; }

    /// <inheritdoc />
    public override bool IsValid(object? value) =>
        value is not string text || !text.Any(character => char.IsControl(character) && !IsAllowedWhitespace(character));

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        AllowLineBreaks
            ? "Remove invisible control characters."
            : "Keep this to one line, without tabs or other control characters.";

    private bool IsAllowedWhitespace(char character) =>
        AllowLineBreaks && character is '\n' or '\r' or '\t';
}
