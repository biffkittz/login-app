using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LoginApp.Api.Features.Auth;

/// <summary>
/// Registers ASP.NET Core Identity, JWT bearer authentication and the auth feature's services.
/// </summary>
internal static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything the <c>/api/auth</c> endpoints need.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthCookieOptions>()
            .BindConfiguration(AuthCookieOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // AddIdentityCore registers only the user-management services. The full AddIdentity would also register
        // Identity's cookie schemes and redirect-to-login behavior, which an API that issues JWTs doesn't want.
        services
            .AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            // AspNetUserManager passes HttpContext.RequestAborted to the store, so database calls for a request
            // the client has abandoned are canceled (the base UserManager always uses CancellationToken.None).
            .AddUserManager<AspNetUserManager<ApplicationUser>>()
            .AddSignInManager()
            .AddErrorDescriber<FriendlyIdentityErrorDescriber>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // AddAuthentication also registers Data Protection, which this app never uses; see InMemoryXmlRepository
        // for why its key ring is kept in memory rather than written to disk. Encrypting keys "at rest" in the
        // same process memory that holds them decrypted would add nothing, so that is stated explicitly.
        services.Configure<KeyManagementOptions>(static options =>
        {
            options.XmlRepository = new InMemoryXmlRepository();
            options.XmlEncryptor = new NullXmlEncryptor();
        });

        // Configured through the options system rather than inline in AddJwtBearer so the bearer handler reads
        // the *validated* JwtOptions, including values that arrive from Azure Key Vault.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IOptions<AuthCookieOptions>>(ConfigureJwtBearer);

        services.AddAuthorization();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<AuthCookieManager>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddAuthRateLimiting();

        return services;
    }

    private static void ConfigureIdentity(IdentityOptions options)
    {
        // The email address is the user name. Identity's default allow-list rejects legitimate addresses such as
        // o'brien@example.com, so permit every character RFC 5322 allows in an unquoted local part.
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.@!#$%&'*+-/=?^_`{|}~";

        options.Password.RequiredLength = PasswordPolicy.MinimumLength;
        options.Password.RequiredUniqueChars = PasswordPolicy.RequiredUniqueChars;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        // The app sends no email, so accounts cannot be confirmed.
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
    }

    private static void ConfigureJwtBearer(
        JwtBearerOptions bearer,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AuthCookieOptions> cookieOptions)
    {
        JwtOptions jwt = jwtOptions.Value;
        string cookieName = cookieOptions.Value.Name;

        // Keep the registered claim names ("sub", "email", "name", "exp") instead of the long WS-Federation
        // claim-type URIs the handler maps them to by default.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.CreateSigningKey(),
            // Pinning the algorithm rules out algorithm-substitution attacks.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Name,
        };

        bearer.Events = new JwtBearerEvents
        {
            // The token lives in an HttpOnly cookie that the SPA never touches, so read it from there. Callers that
            // send a standard Authorization header instead (API tools, tests) keep working.
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(cookieName, out string? token) && !string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    }
}
