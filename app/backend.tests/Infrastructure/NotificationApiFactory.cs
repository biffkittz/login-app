using LoginApp.Api.Infrastructure.Email;
using LoginApp.Api.Tests.Features.Auth.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Tests.Infrastructure;

/// <summary>
/// A <see cref="LoginApiFactory"/> that emails registrations, as the app does in Azure: email and a recipient are
/// configured, and <see cref="Sender"/> stands in for Cloudflare, so no test sends real email.
/// </summary>
public sealed class NotificationApiFactory : LoginApiFactory
{
    /// <summary>The address that hears about every registration.</summary>
    public const string Recipient = "owner@example.com";

    /// <summary>The site the emails name.</summary>
    public const string SiteUrl = "https://grocery.example.com";

    /// <summary>Gets the stand-in for Cloudflare Email Service.</summary>
    internal FakeEmailSender Sender { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:AccountId", "0123456789abcdef0123456789abcdef");
        builder.UseSetting("Email:ApiToken", "test-email-token");
        builder.UseSetting("Email:SenderAddress", "notifications@example.com");
        builder.UseSetting("RegistrationNotifications:Recipients:0", Recipient);
        builder.UseSetting("RegistrationNotifications:SiteUrl", SiteUrl);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Sender);
        });
    }
}
