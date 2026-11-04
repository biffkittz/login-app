using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// Applies EF Core migrations at startup.
/// </summary>
/// <remarks>
/// Migrating at startup keeps deployment to a single step: the database is private to the VNet, so a CI agent
/// outside the network cannot reach it to run migrations. Teams that prefer a separate step can set
/// <c>Database__MigrateOnStartup=false</c> and run a migration bundle instead (see docs/BUILD_AND_DEPLOY.md).
/// </remarks>
internal static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies any pending migrations when <see cref="DatabaseOptions.MigrateOnStartup"/> is enabled.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="cancellationToken">Cancels the migration; it is also bounded by
    /// <see cref="DatabaseOptions.MigrationTimeout"/>.</param>
    /// <returns>A task that completes once the schema is current.</returns>
    /// <exception cref="OperationCanceledException">The timeout elapsed or <paramref name="cancellationToken"/>
    /// was canceled, for example because the database is unreachable.</exception>
    public static async Task MigrateDatabaseIfEnabledAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrationExtensions));
        DatabaseOptions options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (!options.MigrateOnStartup)
        {
            DatabaseLog.MigrationsSkipped(logger);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.MigrationTimeout);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        DatabaseLog.ApplyingMigrations(logger);

        // EF Core 9+ holds a database-wide lock while migrating, so when several replicas start at once the
        // pending migrations are applied exactly once and the other replicas simply wait for them.
        await dbContext.Database.MigrateAsync(timeout.Token);

        DatabaseLog.MigrationsApplied(logger);
    }
}
