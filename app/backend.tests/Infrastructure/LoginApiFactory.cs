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
/// Hosts the real API pipeline in memory for integration tests, with PostgreSQL swapped for a throwaway SQLite
/// database so the suite needs no external services.
/// </summary>
/// <remarks>
/// <para>
/// The "Testing" environment loads only appsettings.json, so tests exercise the production defaults: the
/// <c>__Host-</c>, <c>Secure</c> auth cookie included. Clients therefore use an https base address, otherwise
/// the cookie container would (correctly) refuse to send a Secure cookie.
/// </para>
/// <para>
/// The database is a temporary file rather than SQLite's in-memory mode. An in-memory database can only be shared
/// through a single connection, which can't serve two threads at once, and the realtime hub queries the database
/// while HTTP requests do. A file lets every DbContext open its own connection, as with PostgreSQL.
/// </para>
/// <para>
/// SQLite keeps the suite fast and hermetic, but it is not PostgreSQL. The real migrations are exercised by
/// buildAndRunSite.sh and in deployment, which apply them to an actual PostgreSQL server.
/// </para>
/// <para>
/// No Gemini API key is configured, so items never wait for a picture and nothing calls a paid API.
/// <c>PictureApiFactory</c> adds a key and a stand-in for Gemini.
/// </para>
/// </remarks>
public class LoginApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>A throwaway 384-bit signing key used only by the tests.</summary>
    public const string TestSigningKey = "tzFgVZ1NKHOPM9fFJYqTZlZ/sWmc/SgrU/2l9oXT4YoU65GAA3pYPKUhIUrjxF3k";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"loginapp-tests-{Guid.NewGuid():N}.db");

    // Without pooling, no connection outlives its DbContext, so the file can be deleted when the factory is disposed.
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Pooling = false,
    }.ToString();

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
        // The schema is created from the model because the EF migrations target PostgreSQL. It must exist before the
        // host starts (on first use of Services or a client), as the app's database migration would in production:
        // the picture worker reads the database as soon as it runs.
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        await using ApplicationDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        // Write-ahead logging lets readers carry on while another connection writes, instead of failing with
        // "database is locked". The setting is stored in the file, so it applies to every later connection.
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'wal';", TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (string file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
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

        // Every test signs in from the same "client", so lift the per-client limits; the rate-limiting tests lower them.
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:GroceryList:PermitLimit", "10000");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers the Npgsql setup as an IDbContextOptionsConfiguration (EF Core 9+), so both it
            // and the built options must be removed before registering SQLite, or EF sees two providers.
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(ConnectionString));
        });
    }
}
