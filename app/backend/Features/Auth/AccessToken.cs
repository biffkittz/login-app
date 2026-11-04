namespace LoginApp.Api.Features.Auth;

/// <summary>
/// A signed access token and the instant it stops being valid.
/// </summary>
/// <remarks>
/// Deliberately a class rather than a record: records synthesize a <c>ToString()</c> that prints every property,
/// which would leak the bearer token into any log line that formats this object.
/// </remarks>
/// <param name="value">The compact-serialized JWT.</param>
/// <param name="expiresAt">When the token expires.</param>
internal sealed class AccessToken(string value, DateTimeOffset expiresAt)
{
    /// <summary>Gets the compact-serialized JWT. Treat it as a secret.</summary>
    public string Value { get; } = value;

    /// <summary>Gets the instant the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
}
