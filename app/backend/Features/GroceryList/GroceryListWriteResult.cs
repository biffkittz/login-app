using System.Diagnostics.CodeAnalysis;
using LoginApp.Api.Features.GroceryList.Contracts;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// The result of a write to the grocery list. Expected failures (a missing item, a full list) are values rather than
/// exceptions, so the endpoint can map each one to an HTTP response explicitly.
/// </summary>
internal sealed class GroceryListWriteResult
{
    private GroceryListWriteResult(GroceryListWriteStatus status, GroceryListChange? change)
    {
        Status = status;
        Change = change;
    }

    /// <summary>Gets the outcome of the write.</summary>
    public GroceryListWriteStatus Status { get; }

    /// <summary>Gets what the write changed; non-null exactly when <see cref="Succeeded"/> is true.</summary>
    public GroceryListChange? Change { get; }

    /// <summary>Gets a value indicating whether the write succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Change))]
    public bool Succeeded => Status == GroceryListWriteStatus.Succeeded;

    /// <summary>Creates a successful result.</summary>
    /// <param name="change">What the write changed.</param>
    /// <returns>A successful result.</returns>
    public static GroceryListWriteResult Success(GroceryListChange change) => new(GroceryListWriteStatus.Succeeded, change);

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">Why the write failed; anything but <see cref="GroceryListWriteStatus.Succeeded"/>.</param>
    /// <returns>A failed result.</returns>
    public static GroceryListWriteResult Failure(GroceryListWriteStatus status)
    {
        if (status == GroceryListWriteStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A failure needs a failure status.");
        }

        return new GroceryListWriteResult(status, change: null);
    }
}
