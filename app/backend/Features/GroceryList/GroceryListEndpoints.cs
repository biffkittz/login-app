using System.Security.Claims;
using LoginApp.Api.Features.Auth;
using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LoginApp.Api.Features.GroceryList;

/// <summary>
/// Maps the <c>/api/grocery-list</c> endpoints and the realtime hub. Handlers only translate between HTTP and
/// <see cref="IGroceryListService"/>, which owns the list logic.
/// </summary>
/// <remarks>
/// <para>
/// Every write answers with a <see cref="GroceryListChange"/>, the same message the hub broadcasts, so the SPA
/// handles its own changes and everyone else's with one code path.
/// </para>
/// <para>
/// Request bodies are validated before a handler runs by the minimal API validation filter (<c>AddValidation</c>
/// in Program.cs). Like the auth endpoints, these rely on the <c>SameSite=Strict</c> cookie and JSON-only bodies
/// against cross-site request forgery (see <c>AuthCookieManager</c>).
/// </para>
/// </remarks>
internal static class GroceryListEndpoints
{
    private const string RoutePrefix = "/api/grocery-list";

    /// <summary>
    /// Maps the list, item and hub endpoints. All of them require a signed-in user.
    /// </summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapGroceryListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder list = endpoints.MapGroup(RoutePrefix)
            .WithTags("Grocery list")
            .RequireAuthorization();

        list.MapGet(string.Empty, GetListAsync)
            .WithName("GetGroceryList")
            .WithSummary("Returns every item on the shared grocery list, in order.");

        RouteGroupBuilder items = list.MapGroup("/items").RequireRateLimiting(GroceryListRateLimiting.PolicyName);

        items.MapPost(string.Empty, CreateItemAsync)
            .WithName("CreateGroceryItem")
            .WithSummary("Adds an item to the top of the list, or after another item.");

        items.MapPatch("/{itemId:guid}", UpdateItemAsync)
            .WithName("UpdateGroceryItem")
            .WithSummary("Changes the fields present in the body; an empty string clears an optional field.");

        items.MapPost("/{itemId:guid}/move", MoveItemAsync)
            .WithName("MoveGroceryItem")
            .WithSummary("Moves an item to the top of the list, or directly after another item.");

        items.MapDelete("/{itemId:guid}", DeleteItemAsync)
            .WithName("DeleteGroceryItem")
            .WithSummary("Removes an item from the list.");

        // [Authorize] on the hub covers the connection itself. The rate limit applies per connection attempt, which
        // a well-behaved client makes only when it opens the list or reconnects.
        endpoints.MapHub<GroceryListHub>(GroceryListHub.Path, ConfigureHub)
            .RequireRateLimiting(GroceryListRateLimiting.PolicyName);

        return endpoints;
    }

    private static void ConfigureHub(HttpConnectionDispatcherOptions options)
    {
        // WebSockets only: the SPA skips SignalR's negotiation round trip, which matters on a slow mobile network.
        // Cloudflare and the Container Apps TCP ingress both pass WebSockets through.
        options.Transports = HttpTransportType.WebSockets;

        // Without this an open connection would outlive the access token that authorized it, and keep receiving the
        // list after the session has ended.
        options.CloseOnAuthenticationExpiration = true;
    }

    private static async Task<Ok<GroceryListResponse>> GetListAsync(
        IGroceryListService groceryList,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await groceryList.GetListAsync(cancellationToken));

    private static async Task<Results<Created<GroceryListChange>, ValidationProblem, ProblemHttpResult>> CreateItemAsync(
        CreateGroceryItemRequest request,
        ClaimsPrincipal principal,
        IGroceryListService groceryList,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UserIdOf(principal) is not string userId)
        {
            return SessionEnded(authCookie, response);
        }

        GroceryListWriteResult result = await groceryList.CreateItemAsync(userId, request, cancellationToken);

        // There is no endpoint for a single item to point a Location header at: clients read the whole list.
        return result switch
        {
            { Succeeded: true } => TypedResults.Created((string?)null, result.Change),
            { Status: GroceryListWriteStatus.UnknownStore } => UnknownStore(),
            _ => ToProblem(result.Status, authCookie, response),
        };
    }

    private static async Task<Results<Ok<GroceryListChange>, ValidationProblem, ProblemHttpResult>> UpdateItemAsync(
        Guid itemId,
        UpdateGroceryItemRequest request,
        ClaimsPrincipal principal,
        IGroceryListService groceryList,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UserIdOf(principal) is not string userId)
        {
            return SessionEnded(authCookie, response);
        }

        GroceryListWriteResult result = await groceryList.UpdateItemAsync(userId, itemId, request, cancellationToken);
        return result switch
        {
            { Succeeded: true } => TypedResults.Ok(result.Change),
            { Status: GroceryListWriteStatus.UnknownStore } => UnknownStore(),
            _ => ToProblem(result.Status, authCookie, response),
        };
    }

    private static async Task<Results<Ok<GroceryListChange>, ProblemHttpResult>> MoveItemAsync(
        Guid itemId,
        MoveGroceryItemRequest request,
        ClaimsPrincipal principal,
        IGroceryListService groceryList,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UserIdOf(principal) is not string userId)
        {
            return SessionEnded(authCookie, response);
        }

        GroceryListWriteResult result = await groceryList.MoveItemAsync(userId, itemId, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Change) : ToProblem(result.Status, authCookie, response);
    }

    private static async Task<Results<Ok<GroceryListChange>, ProblemHttpResult>> DeleteItemAsync(
        Guid itemId,
        ClaimsPrincipal principal,
        IGroceryListService groceryList,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UserIdOf(principal) is not string userId)
        {
            return SessionEnded(authCookie, response);
        }

        GroceryListWriteResult result = await groceryList.DeleteItemAsync(userId, itemId, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Change) : ToProblem(result.Status, authCookie, response);
    }

    // Reported like any other invalid field, keyed by the request property, so the form shows it beside the store.
    private static ValidationProblem UnknownStore() =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(UpdateGroceryItemRequest.Store)] = ["Choose one of the stores on the list."],
            },
            title: "We couldn't save that item.");

    // The bearer handler keeps the registered claim names (MapInboundClaims = false), so the ID is in "sub".
    private static string? UserIdOf(ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } userId ? userId : null;

    private static ProblemHttpResult ToProblem(GroceryListWriteStatus status, AuthCookieManager authCookie, HttpResponse response) =>
        status switch
        {
            GroceryListWriteStatus.UnknownUser => SessionEnded(authCookie, response),
            GroceryListWriteStatus.ItemNotFound => Problem(
                StatusCodes.Status404NotFound,
                "That item isn't on the list any more. Someone may have just removed it."),
            GroceryListWriteStatus.DuplicateItemId => Problem(
                StatusCodes.Status409Conflict,
                "An item with this ID already exists."),
            GroceryListWriteStatus.AnchorNotFound => Problem(
                StatusCodes.Status409Conflict,
                "The list changed while you were moving that item. Please try again."),
            GroceryListWriteStatus.ConcurrentChange => Problem(
                StatusCodes.Status409Conflict,
                "Someone changed that item at the same moment. Please try again."),
            GroceryListWriteStatus.ListFull => Problem(
                StatusCodes.Status409Conflict,
                "The list is full. Remove a few items before adding more."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a failure status."),
        };

    // The token is genuine but its account is gone (or the token has no subject): drop the useless cookie, as
    // /api/auth/me does, so the SPA returns to the login page.
    private static ProblemHttpResult SessionEnded(AuthCookieManager authCookie, HttpResponse response)
    {
        authCookie.Delete(response);
        return Problem(StatusCodes.Status401Unauthorized, "Your session has ended. Please sign in again.");
    }

    private static ProblemHttpResult Problem(int statusCode, string title) =>
        TypedResults.Problem(title: title, statusCode: statusCode);
}
