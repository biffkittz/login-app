namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The signals a grocery run's workflow accepts (see <see cref="GroceryRunWorkflow"/>).
/// </summary>
internal enum GroceryRunSignalKind
{
    /// <summary>A participant has nothing more to add (<see cref="GroceryRunWorkflow.ImGoodAsync"/>).</summary>
    ImGood,

    /// <summary>The shopper leaves now (<see cref="GroceryRunWorkflow.StartShoppingNowAsync"/>).</summary>
    StartShoppingNow,

    /// <summary>The shopper calls the run off (<see cref="GroceryRunWorkflow.CancelAsync"/>).</summary>
    Cancel,

    /// <summary>The shopper is done (<see cref="GroceryRunWorkflow.FinishShoppingAsync"/>).</summary>
    FinishShopping,
}
