using System.Text.Json.Serialization;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Where a grocery run is. A run goes from <see cref="LastCall"/> to <see cref="Shopping"/> and ends as
/// <see cref="Finished"/> or <see cref="Expired"/>, or ends early as <see cref="Cancelled"/>.
/// </summary>
/// <remarks>
/// The JSON names are part of the API, of the realtime messages and of the workflow's history in Temporal. Renaming one
/// would stop runs already in flight from replaying, so don't.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<GroceryRunStatus>))]
public enum GroceryRunStatus
{
    /// <summary>
    /// "Speak now or forever hold your peace": everyone can still add items, but nobody can remove or move them.
    /// </summary>
    [JsonStringEnumMemberName("lastCall")]
    LastCall,

    /// <summary>The list is final and the shopper is at the store. The list is open for any change again.</summary>
    [JsonStringEnumMemberName("shopping")]
    Shopping,

    /// <summary>The shopper said they were done.</summary>
    [JsonStringEnumMemberName("finished")]
    Finished,

    /// <summary>Nobody said the shopping was done, so the run ended by itself after its time limit.</summary>
    [JsonStringEnumMemberName("expired")]
    Expired,

    /// <summary>The shopper called the run off during last call, or an operator cancelled the workflow.</summary>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}
