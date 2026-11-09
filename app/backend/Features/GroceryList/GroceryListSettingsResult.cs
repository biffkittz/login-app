using System.Diagnostics.CodeAnalysis;
using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The result of changing the list's settings: the settings as saved, or why they weren't changed.
/// </summary>
internal sealed class GroceryListSettingsResult
{
    private GroceryListSettingsResult(GroceryListWriteStatus status, GroceryListSettingsResponse? settings)
    {
        Status = status;
        Settings = settings;
    }

    /// <summary>Gets the outcome.</summary>
    public GroceryListWriteStatus Status { get; }

    /// <summary>Gets the settings as they are now; non-null exactly when <see cref="Succeeded"/> is true.</summary>
    public GroceryListSettingsResponse? Settings { get; }

    /// <summary>Gets a value indicating whether the change succeeded (or there was nothing to change).</summary>
    [MemberNotNullWhen(true, nameof(Settings))]
    public bool Succeeded => Status == GroceryListWriteStatus.Succeeded;

    /// <summary>Creates a successful result.</summary>
    /// <param name="settings">The settings as they are now.</param>
    /// <returns>A successful result.</returns>
    public static GroceryListSettingsResult Success(GroceryListSettingsResponse settings) =>
        new(GroceryListWriteStatus.Succeeded, settings);

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">Why the change failed; anything but <see cref="GroceryListWriteStatus.Succeeded"/>.</param>
    /// <returns>A failed result.</returns>
    public static GroceryListSettingsResult Failure(GroceryListWriteStatus status) =>
        status == GroceryListWriteStatus.Succeeded
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "A failure needs a failure status.")
            : new GroceryListSettingsResult(status, settings: null);
}
