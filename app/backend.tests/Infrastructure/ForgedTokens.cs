using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// Creates JWTs that look legitimate but are signed with a random key, to prove the API rejects them.
/// </summary>
internal static class ForgedTokens
{
    /// <summary>
    /// Creates a token with the API's issuer and audience but an unknown signing key.
    /// </summary>
    /// <param name="subject">The <c>sub</c> claim to forge.</param>
    /// <returns>The compact-serialized token.</returns>
    public static string Create(string subject)
    {
        SymmetricSecurityKey attackerKey = new(RandomNumberGenerator.GetBytes(32));
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = "login-app",
            Audience = "login-app",
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(attackerKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = subject },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
