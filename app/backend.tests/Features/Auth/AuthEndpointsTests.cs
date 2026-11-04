using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.Auth.Contracts;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LoginApp.Api.Tests.Features.Auth;

/// <summary>
/// End-to-end behavior of the <c>/api/auth</c> endpoints through the real middleware pipeline.
/// </summary>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class AuthEndpointsTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_WithValidDetails_ReturnsCreatedUserAndSetsHardenedAuthCookie()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();
        string email = TestAccounts.UniqueEmail();

        // Act
        using HttpResponseMessage response = await TestAccounts.RegisterAsync(client, email, displayName: "Ada Lovelace");

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        UserResponse? user = await response.Content.ReadFromJsonAsync<UserResponse>(CancellationToken);
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.True(user.SessionExpiresAt > DateTimeOffset.UtcNow);

        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-LoginApp.Auth=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WithInvalidFields_ReturnsValidationProblemForEachField()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { displayName = "", email = "not-an-email", password = "short" },
            CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Contains("DisplayName", problem.Errors.Keys);
        Assert.Contains("Email", problem.Errors.Keys);
        Assert.Contains("Password", problem.Errors.Keys);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Register_WithExistingEmailInDifferentCase_ReturnsSingleFriendlyEmailError()
    {
        // Arrange
        string email = TestAccounts.UniqueEmail();
        using HttpClient firstClient = factory.CreateBrowserLikeClient();
        using HttpResponseMessage first = await TestAccounts.RegisterAsync(firstClient, email);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using HttpClient secondClient = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await TestAccounts.RegisterAsync(secondClient, email.ToUpperInvariant());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(["An account with this email address already exists."], problem.Errors["Email"]);
    }

    [Fact]
    public async Task Register_WithApostropheInEmail_Succeeds()
    {
        // Arrange: Identity's default user-name allow-list would reject this perfectly valid address.
        using HttpClient client = factory.CreateBrowserLikeClient();
        string email = $"o'brien-{Guid.NewGuid():N}@example.com";

        // Act
        using HttpResponseMessage response = await TestAccounts.RegisterAsync(client, email);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_SignsInSoThatMeReturnsTheUser()
    {
        // Arrange
        string email = TestAccounts.UniqueEmail();
        using HttpClient registrationClient = factory.CreateBrowserLikeClient();
        using HttpResponseMessage registration = await TestAccounts.RegisterAsync(registrationClient, email);
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act: email lookup ignores case and surrounding whitespace.
        using HttpResponseMessage login = await TestAccounts.LoginAsync(client, $"  {email.ToUpperInvariant()}  ");
        using HttpResponseMessage me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        UserResponse? user = await me.Content.ReadFromJsonAsync<UserResponse>(CancellationToken);
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
    }

    [Fact]
    public async Task Login_WithWrongPasswordOrUnknownEmail_ReturnsIndistinguishableUnauthorizedProblems()
    {
        // Arrange
        string email = TestAccounts.UniqueEmail();
        using HttpClient client = factory.CreateBrowserLikeClient();
        using HttpResponseMessage registration = await TestAccounts.RegisterAsync(client, email);

        // Act
        using HttpResponseMessage wrongPassword = await TestAccounts.LoginAsync(client, email, "definitely not the password");
        using HttpResponseMessage unknownEmail = await TestAccounts.LoginAsync(client, TestAccounts.UniqueEmail());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        ProblemDetails? wrongPasswordProblem = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        ProblemDetails? unknownEmailProblem = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Equal("Incorrect email or password.", wrongPasswordProblem?.Title);
        Assert.Equal(wrongPasswordProblem?.Title, unknownEmailProblem?.Title);
    }

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksAccountEvenForCorrectPassword()
    {
        // Arrange
        string email = TestAccounts.UniqueEmail();
        using HttpClient client = factory.CreateBrowserLikeClient();
        using HttpResponseMessage registration = await TestAccounts.RegisterAsync(client, email);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await TestAccounts.LoginAsync(client, email, "definitely not the password");
        }

        // Act
        using HttpResponseMessage response = await TestAccounts.LoginAsync(client, email);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.StartsWith("Too many failed sign-in attempts", problem?.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithFormEncodedBody_IsRejectedAsUnsupportedMediaType()
    {
        // Arrange: a cross-site HTML form can only send form/text bodies, never application/json.
        using HttpClient client = factory.CreateBrowserLikeClient();
        using FormUrlEncodedContent form = new(
        [
            new KeyValuePair<string, string>("email", "someone@example.com"),
            new KeyValuePair<string, string>("password", TestAccounts.ValidPassword),
        ]);

        // Act
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/auth/login", UriKind.Relative), form, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutAuthCookie_ReturnsUnauthorized()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithForgedToken_ReturnsUnauthorized()
    {
        // Arrange: a structurally valid JWT signed with a key the server does not know.
        using HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        string forgedToken = ForgedTokens.Create(subject: "any-user");
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"__Host-LoginApp.Auth={forgedToken}");

        // Act
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WhenSignedIn_DeletesCookieSoMeIsUnauthorized()
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();
        using HttpResponseMessage registration = await TestAccounts.RegisterAsync(client, TestAccounts.UniqueEmail());

        // Act
        using HttpResponseMessage logout = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), content: null, CancellationToken);
        using HttpResponseMessage me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        string deletion = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-LoginApp.Auth=;", deletion, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutSession_ReturnsUnauthorized()
    {
        // Arrange: a cross-site form POST never carries the SameSite=Strict cookie, so it lands here.
        using HttpClient client = factory.CreateBrowserLikeClient();

        // Act
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), content: null, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
