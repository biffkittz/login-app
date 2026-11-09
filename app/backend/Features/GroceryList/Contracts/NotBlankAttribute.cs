using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// For optional values where <see langword="null"/> means "leave unchanged": accepts <see langword="null"/> but
/// rejects an empty or whitespace-only string, which would otherwise erase a value that must always be present.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
internal sealed class NotBlankAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value) => value is not string text || !string.IsNullOrWhiteSpace(text);
}
