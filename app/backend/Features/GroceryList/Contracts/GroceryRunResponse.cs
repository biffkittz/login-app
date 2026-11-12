using LoginApp.Api.Features.GroceryList.Runs;

namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// A grocery run as <c>GET /api/grocery-list</c> and the realtime hub (<c>RunChanged</c>) send it.
/// </summary>
/// <param name="Id">The run's ID.</param>
/// <param name="Sequence">Increases with every change to the run. Clients keep the copy with the highest sequence.</param>
/// <param name="Status">Where the run is.</param>
/// <param name="ShopperId">Who is going shopping, or <see langword="null"/> if that account no longer exists.</param>
/// <param name="ShopperName">The shopper's display name, or <see langword="null"/>.</param>
/// <param name="StartedAt">When the run, and last call, started.</param>
/// <param name="LastCallEndsAt">When last call ends unless everyone is ready sooner.</param>
/// <param name="ShoppingStartedAt">When the shopping began, if it has.</param>
/// <param name="EndedAt">When the run ended, if it has.</param>
/// <param name="LastCallOutcome">Why last call ended, once it has.</param>
/// <param name="Participants">The people asked during last call, by name, and whether each said "I'm good".</param>
public sealed record GroceryRunResponse(
    Guid Id,
    int Sequence,
    GroceryRunStatus Status,
    string? ShopperId,
    string? ShopperName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastCallEndsAt,
    DateTimeOffset? ShoppingStartedAt,
    DateTimeOffset? EndedAt,
    LastCallOutcome? LastCallOutcome,
    IReadOnlyList<GroceryRunParticipantResponse> Participants);
