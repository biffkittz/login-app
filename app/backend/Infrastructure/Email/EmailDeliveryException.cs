namespace LoginApp.Api.Infrastructure.Email;

/// <summary>
/// Cloudflare didn't take the email: its API refused it, failed, or couldn't be reached.
/// </summary>
/// <remarks>
/// Messages are written to the log, so they say what went wrong without the addresses or the text of the email.
/// </remarks>
public sealed class EmailDeliveryException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EmailDeliveryException"/> class.</summary>
    public EmailDeliveryException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmailDeliveryException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public EmailDeliveryException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmailDeliveryException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EmailDeliveryException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="isTransient">Whether sending again shortly may well succeed.</param>
    public EmailDeliveryException(string message, bool isTransient)
        : base(message) => IsTransient = isTransient;

    /// <summary>Initializes a new instance of the <see cref="EmailDeliveryException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    /// <param name="isTransient">Whether sending again shortly may well succeed.</param>
    public EmailDeliveryException(string message, Exception innerException, bool isTransient)
        : base(message, innerException) => IsTransient = isTransient;

    /// <summary>
    /// Gets a value indicating whether sending again shortly may well succeed: Cloudflare was unreachable, busy or rate
    /// limiting, rather than refusing the email itself.
    /// </summary>
    public bool IsTransient { get; }
}
