using Microsoft.AspNetCore.Identity;

namespace LoginApp.Api.Infrastructure.Data;

/// <summary>
/// An account that can sign in to the application: the ASP.NET Core Identity user plus the profile data this app
/// displays.
/// </summary>
/// <remarks>
/// The email address doubles as the Identity user name, so <see cref="IdentityUser{TKey}.UserName"/> and
/// <see cref="IdentityUser{TKey}.Email"/> always hold the same value.
/// </remarks>
internal sealed class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The maximum length of <see cref="DisplayName"/>, shared by request validation and the database schema.
    /// </summary>
    public const int DisplayNameMaxLength = 64;

    /// <summary>
    /// Gets or sets the friendly name shown to the user once they are signed in.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}
