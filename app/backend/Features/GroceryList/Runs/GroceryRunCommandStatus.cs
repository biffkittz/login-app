namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// The outcome of a request to start or signal a grocery run.
/// </summary>
internal enum GroceryRunCommandStatus
{
    /// <summary>Temporal took the request; the run's change arrives as a realtime message.</summary>
    Accepted,

    /// <summary>The token is genuine but its account no longer exists.</summary>
    UnknownUser,

    /// <summary>Temporal isn't configured on this server.</summary>
    NotAvailable,

    /// <summary>Temporal couldn't be reached.</summary>
    Unreachable,

    /// <summary>The requested length of last call isn't one of those offered.</summary>
    InvalidLastCall,

    /// <summary>A run is already under way (when starting one).</summary>
    AlreadyUnderWay,

    /// <summary>The run named isn't under way (any more).</summary>
    RunOver,

    /// <summary>Only the shopper may do that.</summary>
    NotShopper,

    /// <summary>The user wasn't asked during this run's last call (they didn't have the list open when it started).</summary>
    NotAsked,

    /// <summary>Last call is over, so it is too late for that.</summary>
    LastCallOver,

    /// <summary>The shopping hasn't begun, so it can't be finished.</summary>
    NotShoppingYet,
}
