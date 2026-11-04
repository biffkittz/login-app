using System.Security.Cryptography;
using LoginApp.Api.Features.Auth;
using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Tests.Features.Auth;

/// <summary>
/// Unit tests for <see cref="JwtAccessTokenIssuer"/>.
/// </summary>
public sealed class JwtAccessTokenIssuerTests
{
    private static readonly JwtOptions s_options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpiryMinutes = 30,
        SigningKey = LoginApiFactory.TestSigningKey,
    };

    private static readonly ApplicationUser s_user = new()
    {
        Id = "user-123",
        Email = "ada@example.com",
        UserName = "ada@example.com",
        DisplayName = "Ada Lovelace",
    };

    [Fact]
    public async Task Issue_ProducesValidTokenWithUserClaimsAndConfiguredLifetime()
    {
        // Arrange: JWT times have one-second resolution, so start the clock on a whole second.
        FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        JwtAccessTokenIssuer issuer = new(Options.Create(s_options), clock);

        // Act
        AccessToken token = issuer.Issue(s_user);

        // Assert
        TokenValidationResult result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(token.Value, CreateValidationParameters(s_options.CreateSigningKey()));
        Assert.True(result.IsValid, result.Exception?.Message);

        JsonWebToken jwt = Assert.IsType<JsonWebToken>(result.SecurityToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
        Assert.Equal("user-123", jwt.Subject);
        Assert.Equal("ada@example.com", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Ada Lovelace", jwt.GetClaim(JwtRegisteredClaimNames.Name).Value);
        Assert.Equal(clock.GetUtcNow().AddMinutes(30), token.ExpiresAt);
        Assert.Equal(token.ExpiresAt.UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public async Task Issue_TokenIsRejectedByAServerWithADifferentKey()
    {
        // Arrange
        JwtAccessTokenIssuer issuer = new(Options.Create(s_options), TimeProvider.System);
        SymmetricSecurityKey otherKey = new(RandomNumberGenerator.GetBytes(32));

        // Act
        AccessToken token = issuer.Issue(s_user);

        // Assert
        TokenValidationResult result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(token.Value, CreateValidationParameters(otherKey));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Issue_GivesEveryTokenAUniqueId()
    {
        // Arrange
        JwtAccessTokenIssuer issuer = new(Options.Create(s_options), TimeProvider.System);
        JsonWebTokenHandler handler = new();

        // Act
        string firstId = handler.ReadJsonWebToken(issuer.Issue(s_user).Value).Id;
        string secondId = handler.ReadJsonWebToken(issuer.Issue(s_user).Value).Id;

        // Assert
        Assert.False(string.IsNullOrEmpty(firstId));
        Assert.NotEqual(firstId, secondId);
    }

    private static TokenValidationParameters CreateValidationParameters(SecurityKey key) => new()
    {
        ValidIssuer = s_options.Issuer,
        ValidAudience = s_options.Audience,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };
}
