namespace LoginApp.Api.Infrastructure.Networking;

/// <summary>
/// Source-generated, allocation-free log messages for network access control.
/// </summary>
internal static partial class NetworkLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning,
        Message = "Refused a request from {RemoteAddress} because it is not in Network:InternalNetworks")]
    public static partial void RequestRefused(ILogger logger, string remoteAddress);
}
