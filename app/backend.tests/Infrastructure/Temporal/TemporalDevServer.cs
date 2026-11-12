using Temporalio.Testing;

[assembly: AssemblyFixture(typeof(LoginApp.Api.Tests.Infrastructure.Temporal.TemporalDevServer))]

namespace LoginApp.Api.Tests.Infrastructure.Temporal;

/// <summary>
/// A Temporal development server for the tests that need a real Temporal (the grocery run workflow), shared by the
/// whole test run and started on first use.
/// </summary>
/// <remarks>
/// <para>
/// The server is the Temporal CLI's (<c>temporal server start-dev</c>), found at <c>TEMPORAL_CLI_PATH</c> or on the
/// <c>PATH</c>. The SDK could download one itself, but the tests don't fetch and run unverified binaries: CI installs
/// the CLI with a checked hash, and developers install it once (docs/DEVELOPMENT.md).
/// </para>
/// <para>
/// Without the CLI these tests are skipped, so the rest of the suite runs anywhere. CI sets
/// <c>TEMPORAL_TESTS_REQUIRED=true</c>, which turns a missing CLI into a failure instead.
/// </para>
/// </remarks>
public sealed class TemporalDevServer : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private WorkflowEnvironment? _environment;

    /// <summary>Gets the path of the Temporal CLI, or <see langword="null"/> when it isn't installed.</summary>
    public static string? CliPath { get; } = FindCli();

    /// <summary>
    /// Returns the running server, starting it on first use. Skips the calling test when the CLI isn't installed
    /// (fails it when <c>TEMPORAL_TESTS_REQUIRED</c> is set).
    /// </summary>
    /// <returns>The server's environment, whose client is connected to the <c>default</c> namespace.</returns>
    public async Task<WorkflowEnvironment> GetAsync()
    {
        if (CliPath is null)
        {
            const string Message = "The Temporal CLI isn't installed (TEMPORAL_CLI_PATH or 'temporal' on the PATH); see docs/DEVELOPMENT.md.";
            if (string.Equals(Environment.GetEnvironmentVariable("TEMPORAL_TESTS_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail(Message);
            }

            Assert.Skip(Message);
        }

        await _startLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            _environment ??= await WorkflowEnvironment.StartLocalAsync(new WorkflowEnvironmentStartLocalOptions
            {
                DevServerOptions = new DevServerOptions
                {
                    ExistingPath = CliPath,
                    LogLevel = "error",
                    StartupTimeout = TimeSpan.FromSeconds(30),
                },
            });
            return _environment;
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Gets the server's gRPC address (<c>host:port</c>), for an app configured to use it.</summary>
    /// <param name="environment">The running server.</param>
    /// <returns>The address.</returns>
    public static string AddressOf(WorkflowEnvironment environment) => environment.Client.Connection.Options.TargetHost!;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_environment is not null)
        {
            await _environment.ShutdownAsync();
            await _environment.DisposeAsync();
        }

        _startLock.Dispose();
    }

    private static string? FindCli()
    {
        string? configured = Environment.GetEnvironmentVariable("TEMPORAL_CLI_PATH");
        if (!string.IsNullOrEmpty(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        string executable = OperatingSystem.IsWindows() ? "temporal.exe" : "temporal";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
    }
}
