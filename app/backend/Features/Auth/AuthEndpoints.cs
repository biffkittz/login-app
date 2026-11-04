using System.Security.Claims;
using LoginApp.Api.Features.Auth.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Maps the <c>/api/auth</c> endpoints. Handlers only translate between HTTP (status codes and the auth cookie)
/// and <see cref="IAuthService"/>, which owns the account logic.
/// </summary>
/// <remarks>
/// Request bodies are validated before a handler runs by the minimal API validation filter
/// (<c>AddValidation</c> in Program.cs), which answers invalid input with a 400 validation problem.
/// </remarks>
internal static class AuthEndpoints
{
    private const string RoutePrefix = "/api/auth";

    /// <summary>
    /// Maps the register, login, logout and current-user endpoints.
    /// </summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder auth = endpoints.MapGroup(RoutePrefix).WithTags("Authentication");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithName("Register")
            .WithSummary("Creates an account and signs the new user in.");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.PolicyName)
            .WithName("Login")
            .WithSummary("Signs a user in by setting the HttpOnly auth cookie.");

        // Requiring authentication means a cross-site form POST, which never carries the SameSite=Strict cookie,
        // cannot sign anyone out. An expired session needs no call at all: its cookie expired with the token.
        auth.MapPost("/logout", Logout)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Signs the current user out by deleting the auth cookie.");

        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Returns the signed-in user.");

        return endpoints;
    }

    private static async Task<Results<Created<UserResponse>, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        IAuthService authService,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        RegistrationResult result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.Errors, title: "We couldn't create your account.");
        }

        authCookie.Append(response, result.Session.Token);
        return TypedResults.Created($"{RoutePrefix}/me", result.Session.User);
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        IAuthService authService,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        LoginResult result = await authService.LoginAsync(request, cancellationToken);
        if (result.Succeeded)
        {
            authCookie.Append(response, result.Session.Token);
            return TypedResults.Ok(result.Session.User);
        }

        return result.Status == LoginStatus.LockedOut
            // Only existing accounts can be locked, so this reveals that the email is registered. Registration
            // must reveal that anyway ("email already taken"), so hiding it here would protect nothing while
            // leaving a locked-out user baffled about why their correct password is refused.
            ? TypedResults.Problem(
                title: "Too many failed sign-in attempts. Please wait a few minutes and try again.",
                statusCode: StatusCodes.Status429TooManyRequests)
            // Unknown email and wrong password deliberately share one response.
            : TypedResults.Problem(
                title: "Incorrect email or password.",
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static NoContent Logout(AuthCookieManager authCookie, HttpResponse response)
    {
        authCookie.Delete(response);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        IAuthService authService,
        AuthCookieManager authCookie,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        UserResponse? user = await authService.GetCurrentUserAsync(principal, cancellationToken);
        if (user is null)
        {
            // The token is genuine but its account no longer exists: drop the useless cookie.
            authCookie.Delete(response);
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(user);
    }
}
