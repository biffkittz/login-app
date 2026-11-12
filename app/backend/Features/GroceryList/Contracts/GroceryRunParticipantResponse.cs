namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// Someone asked during a grocery run's last call (see <see cref="GroceryRunResponse.Participants"/>).
/// </summary>
/// <param name="UserId">The participant's user ID, so a phone can tell whether it is its own user.</param>
/// <param name="DisplayName">The participant's display name.</param>
/// <param name="Ready">Whether they said "I'm good".</param>
public sealed record GroceryRunParticipantResponse(string UserId, string DisplayName, bool Ready);
