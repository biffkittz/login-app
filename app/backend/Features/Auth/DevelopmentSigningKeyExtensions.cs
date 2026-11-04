using System.Security.Cryptography;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Lets a fresh clone run in Development without any secret setup.
/// </summary>
internal static class DevelopmentSigningKeyExtensions
{
    private const string SigningKeyPath = $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}";

    /// <summary>
    /// In the Development environment only, adds a random JWT signing key when none is configured. In every
    /// other environment a missing key fails options validation at startup instead.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns><see langword="true"/> if an ephemeral key was added (worth a warning in the log).</returns>
    /// <remarks>
    /// The key lives only as long as the process, so developers are signed out whenever the API restarts. Set a
    /// persistent key with <c>dotnet user-secrets set Jwt:SigningKey "$(openssl rand -base64 48)"</c>.
    /// Committing a "development" key would be simpler, but committed keys have a way of reaching production.
    /// </remarks>
    public static bool AddEphemeralSigningKeyInDevelopment(
        this IConfigurationManager configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || !string.IsNullOrWhiteSpace(configuration[SigningKeyPath]))
        {
            return false;
        }

        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(JwtOptions.MinimumSigningKeyBytes * 2));
        configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(SigningKeyPath, key)]);
        return true;
    }
}
