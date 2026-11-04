using LoginApp.Api.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs with <see cref="JsonWebTokenHandler"/>, the modern, faster replacement for the
/// legacy <c>JwtSecurityTokenHandler</c>. The JWT bearer handler validates them with the same
/// <see cref="JwtOptions"/>.
/// </summary>
/// <remarks>Registered as a singleton; <see cref="JsonWebTokenHandler"/> is safe for concurrent use.</remarks>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtAccessTokenIssuer"/> class.
    /// </summary>
    /// <param name="options">Validated JWT settings.</param>
    /// <param name="timeProvider">The clock used for <c>iat</c>, <c>nbf</c> and <c>exp</c>.</param>
    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _signingCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public AccessToken Issue(ApplicationUser user)
    {
        DateTimeOffset issuedAt = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = issuedAt.AddMinutes(_options.ExpiryMinutes);

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>(capacity: 4)
            {
                [JwtRegisteredClaimNames.Sub] = user.Id,
                [JwtRegisteredClaimNames.Email] = user.Email ?? string.Empty,
                [JwtRegisteredClaimNames.Name] = user.DisplayName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        };

        return new AccessToken(_tokenHandler.CreateToken(descriptor), expiresAt);
    }
}
