using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace LoginApp.Api.Infrastructure.Security;

/// <summary>
/// Keeps the ASP.NET Core Data Protection key ring in process memory instead of on disk.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddAuthentication</c> registers Data Protection, and by default its key ring is written unencrypted to
/// <c>~/.aspnet/DataProtection-Keys</c> at startup, inside the container. This app never protects any data with
/// it: access tokens are JWTs signed with <c>Jwt:SigningKey</c>, and Identity's cookies, token providers and
/// antiforgery are not used. Keeping the keys in memory avoids writing pointless secrets to disk.
/// </para>
/// <para>
/// If a future feature does need Data Protection (antiforgery tokens, password-reset links), replace this with
/// shared persistent storage, for example Azure Blob Storage protected by a Key Vault key. Otherwise each replica
/// and each restart would have different keys.
/// </para>
/// </remarks>
internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_lock)
        {
            // Hand out copies: XElement is mutable and the key manager may read concurrently with a write.
            return _elements.ConvertAll(static element => new XElement(element));
        }
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_lock)
        {
            _elements.Add(new XElement(element));
        }
    }
}
