using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// Registers the application's EF Core context and database settings.
/// </summary>
internal static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ApplicationDbContext"/> backed by PostgreSQL, plus validated <see cref="DatabaseOptions"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplicationDatabase(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .Configure<IConfiguration>(static (options, configuration) =>
                options.ConnectionString =
                    configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The options callback runs when a context is first created rather than now. That way configuration
        // added after registration (Azure Key Vault, test overrides) is honored, and a missing connection
        // string surfaces as the validation message above instead of an obscure Npgsql error.
        services.AddDbContext<ApplicationDbContext>(static (serviceProvider, options) =>
        {
            DatabaseOptions database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            // Retries transient failures (failovers, brief network blips), which managed cloud databases
            // such as Azure Database for PostgreSQL produce routinely.
            options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure());
        });

        return services;
    }
}
