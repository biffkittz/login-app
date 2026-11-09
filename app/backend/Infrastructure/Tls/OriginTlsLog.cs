namespace LoginApp.Api.Infrastructure.Tls;

/// <summary>
/// Source-generated, allocation-free log messages for origin TLS.
/// </summary>
internal static partial class OriginTlsLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning,
        Message = "Refused a request from {RemoteAddress} because its connection presented no client certificate")]
    public static partial void ClientCertificateMissing(ILogger logger, string remoteAddress);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Refused a TLS client certificate issued by {Issuer}: it doesn't chain to a trusted authority, is outside its validity period or isn't meant for client authentication")]
    public static partial void ClientCertificateRejected(ILogger logger, string issuer);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Serving HTTPS with certificate {Subject} (thumbprint {Thumbprint}, expires {NotAfter:u}); Cloudflare client certificates required: {ClientCertificateRequired}")]
    public static partial void ServingHttps(
        ILogger logger, string subject, string thumbprint, DateTime notAfter, bool clientCertificateRequired);
}
