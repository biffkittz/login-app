using System.ComponentModel.DataAnnotations;

namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// Settings for the PostgreSQL database, bound from the <c>Database</c> configuration section.
/// </summary>
internal sealed class DatabaseOptions
{
    /// <summary>The configuration section that holds these options.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The name of the connection string under <c>ConnectionStrings</c>. In Azure it is supplied by Key Vault as
    /// the secret <c>ConnectionStrings--DefaultConnection</c> (or the environment variable
    /// <c>ConnectionStrings__DefaultConnection</c>).
    /// </summary>
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>
    /// Gets or sets the Npgsql connection string. It is copied from
    /// <c>ConnectionStrings:DefaultConnection</c> rather than bound from this section, so the conventional
    /// connection-string location stays the single source of truth.
    /// </summary>
    /// <remarks>Contains a password: never log this object.</remarks>
    [Required(ErrorMessage = "ConnectionStrings:DefaultConnection is not configured. Locally it comes from appsettings.Development.json; in Azure from Key Vault (secret 'ConnectionStrings--DefaultConnection').")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether pending EF Core migrations are applied when the app starts.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets how long startup waits for migrations (including connection retries) before giving up.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:30:00", ParseLimitsInInvariantCulture = true)]
    public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
