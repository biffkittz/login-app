using LoginApp.Api.Infrastructure.Data;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// A grocery run as the app shows it: the database's copy of the state of one <see cref="GroceryRunWorkflow"/>.
/// </summary>
/// <remarks>
/// <para>
/// The workflow in Temporal decides everything about a run; only its activities write this row
/// (<see cref="GroceryRunStore.ApplyAsync"/>). The app reads the row to draw the banner, to answer
/// <c>GET /api/grocery-list</c>, and to lock the list during last call, so none of that waits for Temporal or fails
/// with it.
/// </para>
/// <para>
/// Should the workflow never report its end (an operator terminated it, or Temporal was unreachable for hours), the
/// deadlines on the row still end the run for the app: last call's lock lapses after <see cref="LastCallEndsAt"/> and
/// the run is over after <see cref="ExpiresAt"/>.
/// </para>
/// </remarks>
internal sealed class GroceryRun
{
    /// <summary>Gets or sets the run's ID, chosen by the app when it starts the workflow.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the number of the workflow state this row holds. States are applied only in increasing order, so a
    /// repeated or late activity can't take the run back. Also the optimistic-concurrency token.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>Gets or sets where the run is.</summary>
    public GroceryRunStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the ID of the user going shopping, who started the run; <see langword="null"/> once that account
    /// no longer exists.
    /// </summary>
    public string? ShopperId { get; set; }

    /// <summary>Gets or sets the user identified by <see cref="ShopperId"/>.</summary>
    public ApplicationUser? Shopper { get; set; }

    /// <summary>Gets or sets when the run started, and with it last call.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when last call ends unless everyone is ready sooner.</summary>
    public DateTimeOffset LastCallEndsAt { get; set; }

    /// <summary>Gets or sets when the shopping began; <see langword="null"/> before then, or if it never did.</summary>
    public DateTimeOffset? ShoppingStartedAt { get; set; }

    /// <summary>Gets or sets when the run ended; <see langword="null"/> while it is under way.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the latest moment the run can still be under way: the end of last call plus the shopping time
    /// limit, then the start of the shopping plus that limit. Past it, the app treats the run as over whatever
    /// <see cref="Status"/> says.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets or sets why last call ended; <see langword="null"/> until it has, and for a cancelled run.</summary>
    public LastCallOutcome? LastCallOutcome { get; set; }

    /// <summary>
    /// Gets or sets the people asked during last call: everyone else who had the list open when the run started.
    /// </summary>
    public List<GroceryRunParticipant> Participants { get; set; } = [];

    /// <summary>Gets a value indicating whether the run hasn't ended, according to its status alone.</summary>
    public bool IsUnderWay => Status is GroceryRunStatus.LastCall or GroceryRunStatus.Shopping;
}
