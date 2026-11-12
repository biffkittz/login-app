using System.Text.Json.Serialization;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Why last call ended and the shopping began. Like <see cref="GroceryRunStatus"/>, the JSON names are also in the
/// workflow's history: don't rename them.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LastCallOutcome>))]
public enum LastCallOutcome
{
    /// <summary>Everyone who was asked said "I'm good" before the time was up.</summary>
    [JsonStringEnumMemberName("everyoneReady")]
    EveryoneReady,

    /// <summary>The time was up.</summary>
    [JsonStringEnumMemberName("timeUp")]
    TimeUp,

    /// <summary>The shopper didn't wait for the time to be up.</summary>
    [JsonStringEnumMemberName("shopperWentEarly")]
    ShopperWentEarly,
}
