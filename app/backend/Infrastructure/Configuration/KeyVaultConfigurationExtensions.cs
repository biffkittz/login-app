using Azure.Core;
using Azure.Identity;

namespace LoginApp.Api.Infrastructure.Configuration;

/// <summary>
/// Adds Azure Key Vault as a configuration source when a vault URI is configured.
/// </summary>
/// <remarks>
/// <para>
/// Two ways of getting Key Vault secrets into the app are supported, and the code below is only needed for the
/// second:
/// </para>
/// <list type="number">
///   <item>Azure Container Apps resolves Key Vault references into environment variables
///   (<c>ConnectionStrings__DefaultConnection=secretref:...</c>). No vault access from the app is needed.</item>
///   <item>The app reads the vault itself at startup using its managed identity, enabled by setting
///   <c>KeyVault__Uri</c>. Every secret in the vault becomes configuration.</item>
/// </list>
/// <para>
/// Either way the app sees ordinary configuration keys. Secret names use <c>--</c> where configuration uses
/// <c>:</c>, so the secret <c>ConnectionStrings--DefaultConnection</c> becomes
/// <c>ConnectionStrings:DefaultConnection</c>.
/// </para>
/// </remarks>
internal static class KeyVaultConfigurationExtensions
{
    /// <summary>The configuration key holding the vault URI, e.g. <c>https://kv-login-app.vault.azure.net/</c>.</summary>
    public const string VaultUriKey = "KeyVault:Uri";

    /// <summary>
    /// The configuration key for the client ID of a user-assigned managed identity. When unset, the standard
    /// <c>AZURE_CLIENT_ID</c> variable is used, and when that is unset too, the system-assigned identity.
    /// </summary>
    public const string ManagedIdentityClientIdKey = "KeyVault:ManagedIdentityClientId";

    private const string AzureClientIdKey = "AZURE_CLIENT_ID";

    /// <summary>
    /// Adds the Key Vault configuration provider if <see cref="VaultUriKey"/> is set; otherwise does nothing.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment, used to choose the credential type.</param>
    /// <returns>The same configuration, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The vault URI is not an absolute HTTPS URI.</exception>
    /// <remarks>
    /// This runs before the host is built, and the provider loads every secret immediately, so a missing role
    /// assignment fails startup with an explicit authorization error rather than at the first request.
    /// </remarks>
    public static IConfigurationManager AddAzureKeyVaultIfConfigured(
        this IConfigurationManager configuration,
        IHostEnvironment environment)
    {
        string? vaultUri = configuration[VaultUriKey];
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return configuration;
        }

        if (!Uri.TryCreate(vaultUri, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{VaultUriKey} must be an absolute https:// URI, but was '{vaultUri}'.");
        }

        configuration.AddAzureKeyVault(uri, CreateCredential(configuration, environment));
        return configuration;
    }

    private static TokenCredential CreateCredential(IConfiguration configuration, IHostEnvironment environment)
    {
        // On a developer machine, use the developer's own sign-in (Azure CLI, VS Code, ...). In Azure, use only
        // managed identity: DefaultAzureCredential probes many credential sources, which the Azure SDK guidance
        // discourages in production because it is slower and can silently pick up an unexpected identity.
        if (environment.IsDevelopment())
        {
            return new DefaultAzureCredential();
        }

        string? clientId = configuration[ManagedIdentityClientIdKey] ?? configuration[AzureClientIdKey];
        ManagedIdentityId identity = string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(clientId);

        return new ManagedIdentityCredential(identity);
    }
}
