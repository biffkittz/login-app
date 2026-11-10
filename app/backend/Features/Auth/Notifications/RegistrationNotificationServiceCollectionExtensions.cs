using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoginApp.Api.Features.Auth.Notifications;

/// <summary>
/// Registers the emails about new registrations: the queue registering writes to, the worker that sends them, and the
/// daily limit. Sending itself is <see cref="Infrastructure.Email.IEmailSender"/>, registered by <c>AddEmail</c>.
/// </summary>
internal static class RegistrationNotificationServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IRegistrationNotifier"/> and what sends its emails.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRegistrationNotifications(this IServiceCollection services)
    {
        services.AddOptions<RegistrationNotificationOptions>()
            .BindConfiguration(RegistrationNotificationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RegistrationNotificationQueue>();
        services.AddSingleton<IRegistrationNotifier>(static serviceProvider => serviceProvider.GetRequiredService<RegistrationNotificationQueue>());
        services.AddSingleton<RegistrationNotificationBudget>();
        services.AddHostedService<RegistrationNotificationWorker>();
        return services;
    }
}
