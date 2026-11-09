namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>PATCH /api/grocery-list/settings</c>: only the settings to change.
/// </summary>
public sealed class UpdateGroceryListSettingsRequest
{
    /// <summary>
    /// Gets whether new and renamed items get an AI-drawn picture; <see langword="null"/> leaves it unchanged.
    /// Turning it off also stops the pictures still waiting to be drawn.
    /// </summary>
    public bool? ImageGenerationEnabled { get; init; }
}
