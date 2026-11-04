using System.Net.Http.Json;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Helpers for creating unique accounts, so tests sharing one database never collide.
/// </summary>
internal static class TestAccounts
{
    /// <summary>A password that satisfies the password policy.</summary>
    public const string ValidPassword = "correct horse battery staple";

    /// <summary>
    /// Returns an email address no other test uses.
    /// </summary>
    /// <returns>A unique address under example.com.</returns>
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// Registers an account through the public API.
    /// </summary>
    /// <param name="client">The client to register with; it ends up signed in as the new user.</param>
    /// <param name="email">The email address to register.</param>
    /// <param name="password">The password to register.</param>
    /// <param name="displayName">The display name to register.</param>
    /// <returns>The registration response.</returns>
    public static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        string password = ValidPassword,
        string displayName = "Test User") =>
        client.PostAsJsonAsync(
            "/api/auth/register",
            new { displayName, email, password },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Signs in through the public API.
    /// </summary>
    /// <param name="client">The client to sign in with.</param>
    /// <param name="email">The email address.</param>
    /// <param name="password">The password.</param>
    /// <returns>The login response.</returns>
    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
}
