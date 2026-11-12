using System.Security.Claims;
using LoginApp.Api.Features.Auth;
using LoginApp.Api.Features.GroceryList.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LoginApp.Api.Features.GroceryList.Runs;

/// <summary>
/// Maps the <c>/api/grocery-list/run</c> endpoints: start a grocery run, and the taps on its banner ("I'm good", start
/// shopping now, call it off, done shopping).
/// </summary>
/// <remarks>
/// Each answers <c>202 Accepted</c> once Temporal has the request. What it changes arrives a moment later on every
/// phone at once, as the realtime <c>RunChanged</c> message, when the workflow's activity has saved it.
/// </remarks>
internal static class GroceryRunEndpoints
{
    /// <summary>
    /// Maps the run endpoints into the list's route group (which requires a signed-in user).
    /// </summary>
    /// <param name="list">The <c>/api/grocery-list</c> group.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapGroceryRunEndpoints(this RouteGroupBuilder list)
    {
        RouteGroupBuilder run = list.MapGroup("/run").RequireRateLimiting(GroceryListRateLimiting.PolicyName);

        run.MapPost(string.Empty, StartAsync)
            .WithName("StartGroceryRun")
            .WithSummary("Starts a grocery run with the caller as the shopper: last call for everyone else who has the list open.");

        run.MapPost("/{runId:guid}/ready", (Guid runId, ClaimsPrincipal principal, GroceryRunService runs, AuthCookieManager authCookie, HttpResponse response, CancellationToken cancellationToken) =>
                SignalAsync(GroceryRunSignalKind.ImGood, runId, principal, runs, authCookie, response, cancellationToken))
            .WithName("GroceryRunImGood")
            .WithSummary("\"I'm good\": the caller has nothing more to add. Last call ends early once everyone asked has said so.");

        run.MapPost("/{runId:guid}/start-shopping", (Guid runId, ClaimsPrincipal principal, GroceryRunService runs, AuthCookieManager authCookie, HttpResponse response, CancellationToken cancellationToken) =>
                SignalAsync(GroceryRunSignalKind.StartShoppingNow, runId, principal, runs, authCookie, response, cancellationToken))
            .WithName("StartGroceryRunShopping")
            .WithSummary("The shopper ends last call now and starts shopping.");

        run.MapPost("/{runId:guid}/cancel", (Guid runId, ClaimsPrincipal principal, GroceryRunService runs, AuthCookieManager authCookie, HttpResponse response, CancellationToken cancellationToken) =>
                SignalAsync(GroceryRunSignalKind.Cancel, runId, principal, runs, authCookie, response, cancellationToken))
            .WithName("CancelGroceryRun")
            .WithSummary("The shopper calls the run off during last call.");

        run.MapPost("/{runId:guid}/finish", (Guid runId, ClaimsPrincipal principal, GroceryRunService runs, AuthCookieManager authCookie, HttpResponse response, CancellationToken cancellationToken) =>
                SignalAsync(GroceryRunSignalKind.FinishShopping, runId, principal, runs, authCookie, response, cancellationToken))
            .WithName("FinishGroceryRun")
            .WithSummary("The shopper is done shopping: the run ends.");

        return list;
    }

    private static async Task<Results<Accepted<GroceryRunStartedResponse>, ValidationProblem, ProblemHttpResult>> StartAsync(
        StartGroceryRunRequest request,
        ClaimsPrincipal principal,
        GroceryRunService runs,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (GroceryListEndpoints.UserIdOf(principal) is not string userId)
        {
            return GroceryListEndpoints.SessionEnded(authCookie, response);
        }

        (GroceryRunCommandStatus status, Guid? runId) = await runs.StartAsync(userId, request.LastCallMinutes, cancellationToken);
        return status switch
        {
            GroceryRunCommandStatus.Accepted => TypedResults.Accepted((string?)null, new GroceryRunStartedResponse(runId!.Value)),
            GroceryRunCommandStatus.InvalidLastCall => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(StartGroceryRunRequest.LastCallMinutes)] = ["Choose one of the lengths offered."],
                },
                title: "We couldn't start the grocery run."),
            _ => ToProblem(status, authCookie, response),
        };
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> SignalAsync(
        GroceryRunSignalKind signal,
        Guid runId,
        ClaimsPrincipal principal,
        GroceryRunService runs,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (GroceryListEndpoints.UserIdOf(principal) is not string userId)
        {
            return GroceryListEndpoints.SessionEnded(authCookie, response);
        }

        GroceryRunCommandStatus status = await runs.SignalAsync(userId, runId, signal, cancellationToken);
        return status == GroceryRunCommandStatus.Accepted
            ? TypedResults.Accepted((string?)null)
            : ToProblem(status, authCookie, response);
    }

    private static ProblemHttpResult ToProblem(GroceryRunCommandStatus status, AuthCookieManager authCookie, HttpResponse response) =>
        status switch
        {
            GroceryRunCommandStatus.UnknownUser => GroceryListEndpoints.SessionEnded(authCookie, response),
            GroceryRunCommandStatus.NotAvailable => GroceryListEndpoints.Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Grocery runs aren't set up on this server."),
            GroceryRunCommandStatus.Unreachable => GroceryListEndpoints.Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Grocery runs aren't available right now. Please try again in a minute."),
            GroceryRunCommandStatus.AlreadyUnderWay => GroceryListEndpoints.Problem(
                StatusCodes.Status409Conflict,
                "A grocery run is already under way."),
            GroceryRunCommandStatus.RunOver => GroceryListEndpoints.Problem(
                StatusCodes.Status409Conflict,
                "That grocery run is over."),
            GroceryRunCommandStatus.NotShopper => GroceryListEndpoints.Problem(
                StatusCodes.Status403Forbidden,
                "Only the shopper can do that."),
            GroceryRunCommandStatus.NotAsked => GroceryListEndpoints.Problem(
                StatusCodes.Status409Conflict,
                "Nobody is waiting on you for this run: you didn't have the list open when it started. Add what you need before last call ends."),
            GroceryRunCommandStatus.LastCallOver => GroceryListEndpoints.Problem(
                StatusCodes.Status409Conflict,
                "Last call is over: the list is final."),
            GroceryRunCommandStatus.NotShoppingYet => GroceryListEndpoints.Problem(
                StatusCodes.Status409Conflict,
                "The shopping hasn't started yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a failure status."),
        };
}
