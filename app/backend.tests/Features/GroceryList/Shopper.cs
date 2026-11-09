using System.Net.Http.Headers;
using System.Net.Http.Json;
using LoginApp.Api.Features.Auth.Contracts;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// A registered, signed-in user with an HTTP client that acts as them, plus shortcuts for the grocery list API.
/// </summary>
/// <remarks>
/// The client sends the access token in an <c>Authorization</c> header instead of the cookie, which the API accepts
/// from non-browser callers. That way the same token can also open a realtime connection (<see cref="RealtimeListener"/>).
/// </remarks>
internal sealed class Shopper : IDisposable
{
    private const string ItemsPath = "/api/grocery-list/items";

    private Shopper(string userId, string displayName, string accessToken, HttpClient http)
    {
        UserId = userId;
        DisplayName = displayName;
        AccessToken = accessToken;
        Http = http;
    }

    /// <summary>Gets the user's ID.</summary>
    public string UserId { get; }

    /// <summary>Gets the user's display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the user's access token (the value of the auth cookie).</summary>
    public string AccessToken { get; }

    /// <summary>Gets a client that is signed in as this user.</summary>
    public HttpClient Http { get; }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Registers a new account and returns it signed in.
    /// </summary>
    /// <param name="factory">The API host.</param>
    /// <param name="displayName">The display name to register.</param>
    /// <returns>The signed-in user.</returns>
    public static async Task<Shopper> SignUpAsync(WebApplicationFactory<Program> factory, string displayName = "Shopper")
    {
        HttpClient http = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using HttpResponseMessage response = await TestAccounts.RegisterAsync(http, TestAccounts.UniqueEmail(), displayName: displayName);
        response.EnsureSuccessStatusCode();
        UserResponse user = await response.Content.ReadFromJsonAsync<UserResponse>(CancellationToken)
            ?? throw new InvalidOperationException("Registration returned no user.");

        // Set-Cookie: __Host-LoginApp.Auth=<token>; expires=...; path=/; secure; samesite=strict; httponly
        string cookie = response.Headers.GetValues("Set-Cookie").Single();
        string accessToken = cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..cookie.IndexOf(';', StringComparison.Ordinal)];

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return new Shopper(user.Id, user.DisplayName, accessToken, http);
    }

    /// <summary>Reads the whole list.</summary>
    /// <returns>The list.</returns>
    public async Task<GroceryListResponse> GetListAsync() =>
        await Http.GetFromJsonAsync<GroceryListResponse>("/api/grocery-list", CancellationToken)
        ?? throw new InvalidOperationException("The list was empty.");

    /// <summary>Sends <c>POST /api/grocery-list/items</c>.</summary>
    /// <param name="body">The request body.</param>
    /// <returns>The response.</returns>
    public Task<HttpResponseMessage> PostItemAsync(object body) =>
        Http.PostAsJsonAsync(ItemsPath, body, CancellationToken);

    /// <summary>Sends <c>PATCH /api/grocery-list/items/{itemId}</c>.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="body">The request body.</param>
    /// <returns>The response.</returns>
    public Task<HttpResponseMessage> PatchItemAsync(Guid itemId, object body) =>
        Http.PatchAsJsonAsync($"{ItemsPath}/{itemId}", body, CancellationToken);

    /// <summary>Sends <c>POST /api/grocery-list/items/{itemId}/move</c>.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="afterItemId">The item to move it after, or <see langword="null"/> for the top.</param>
    /// <returns>The response.</returns>
    public Task<HttpResponseMessage> MoveItemAsync(Guid itemId, Guid? afterItemId) =>
        Http.PostAsJsonAsync($"{ItemsPath}/{itemId}/move", new { afterItemId }, CancellationToken);

    /// <summary>Sends <c>DELETE /api/grocery-list/items/{itemId}</c>.</summary>
    /// <param name="itemId">The item.</param>
    /// <returns>The response.</returns>
    public Task<HttpResponseMessage> DeleteItemAsync(Guid itemId) =>
        Http.DeleteAsync(new Uri($"{ItemsPath}/{itemId}", UriKind.Relative), CancellationToken);

    /// <summary>Adds an item and returns the server's copy of it, failing the test if that doesn't work.</summary>
    /// <param name="name">The item's name.</param>
    /// <param name="afterItemId">The item to add it after, or <see langword="null"/> for the top.</param>
    /// <returns>The saved item.</returns>
    public async Task<GroceryItemResponse> AddItemAsync(string name, Guid? afterItemId = null)
    {
        using HttpResponseMessage response = await PostItemAsync(new { id = Guid.NewGuid(), name, afterItemId });
        response.EnsureSuccessStatusCode();
        GroceryListChange change = await ReadChangeAsync(response);
        return Assert.Single(change.SavedItems);
    }

    /// <summary>Reads the change a successful write answered with.</summary>
    /// <param name="response">The response to a write.</param>
    /// <returns>The change.</returns>
    public static async Task<GroceryListChange> ReadChangeAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<GroceryListChange>(CancellationToken)
        ?? throw new InvalidOperationException("The response had no body.");

    /// <inheritdoc />
    public void Dispose() => Http.Dispose();
}
