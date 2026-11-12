namespace LoginApp.Api.Features.GroceryList.Contracts;

/// <summary>
/// The body of <c>202 Accepted</c> from <c>POST /api/grocery-list/run</c>. The run itself arrives a moment later, as a
/// realtime <c>RunChanged</c> message, once its workflow has opened last call.
/// </summary>
/// <param name="RunId">The new run's ID.</param>
public sealed record GroceryRunStartedResponse(Guid RunId);
