using LoginApp.Api.Infrastructure.Data;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Someone asked during a run's last call, and whether they have said "I'm good".
/// </summary>
internal sealed class GroceryRunParticipant
{
    /// <summary>Gets or sets the run's ID.</summary>
    public Guid RunId { get; set; }

    /// <summary>Gets or sets the participant's user ID.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the participant's account.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>Gets or sets a value indicating whether the participant said "I'm good".</summary>
    public bool IsReady { get; set; }
}
