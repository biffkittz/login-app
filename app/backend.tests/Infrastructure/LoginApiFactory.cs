using LoginApp.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline in memory for integration tests, with PostgreSQL swapped for an in-memory SQLite
/// database so the suite needs no external services.
/// </summary>
/// <remarks>
/// <para>
/// The "Testing" environment loads only appsettings.json, so tests exercise the production defaults: the
/// <c>__Host-</c>, <c>Secure</c> auth cookie included. Clients therefore use an https base address, otherwise
/// the cookie container would (correctly) refuse to send a Secure cookie.
/// </para>
/// <para>
/// SQLite keeps the suite fast and hermetic, but it is not PostgreSQL. The real migrations are exercised by
/// buildAndRunSite.sh and in deployment, which apply them to an actual PostgreSQL server.
/// </para>
/// </remarks>
public sealed class LoginApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>A throwaway 384-bit signing key used only by the tests.</summary>
    public const string TestSigningKey = "tzFgVZ1NKHOPM9fFJYqTZlZ/sWmc/SgrU/2l9oXT4YoU65GAA3pYPKUhIUrjxF3k";

    // An in-memory SQLite database lives exactly as long as its first open connection, so one connection is held
    // open for the lifetime of the factory and shared by every DbContext.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>
    /// Creates a client that talks to the in-memory server over "https" and keeps cookies between requests,
    /// like a browser.
    /// </summary>
    /// <returns>A new client with its own cookie jar.</returns>
    public HttpClient CreateBrowserLikeClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);

        // Accessing Services builds the host; the schema is created from the model because the EF migrations
        // target PostgreSQL.
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused-sqlite-replaces-postgresql");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        // The in-memory test server has no TCP peer address, which the internal-network check rightly refuses.
        // The middleware has its own unit tests.
        builder.UseSetting("Network:RestrictToInternalNetworks", "false");

        // Every test signs in from the same "client", so lift the per-client limit; AuthRateLimitingTests lowers it.
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "10000");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers the Npgsql setup as an IDbContextOptionsConfiguration (EF Core 9+), so both it
            // and the built options must be removed before registering SQLite, or EF sees two providers.
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        });
    }
}
