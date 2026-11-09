namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// No usable picture came back: the API refused the request, the model drew nothing (a safety filter, say), or what
/// came back wasn't a picture the app can use.
/// </summary>
/// <remarks>
/// Messages are written to the log, so they say what went wrong without repeating the item's name or the API's own
/// error text, which can quote the request.
/// </remarks>
public sealed class ImageGenerationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ImageGenerationException"/> class.</summary>
    public ImageGenerationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageGenerationException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public ImageGenerationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageGenerationException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ImageGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageGenerationException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="isTransient">Whether asking again shortly may well succeed.</param>
    public ImageGenerationException(string message, bool isTransient)
        : base(message) => IsTransient = isTransient;

    /// <summary>
    /// Gets a value indicating whether asking again shortly may well succeed: the API was overloaded or rate-limited
    /// us, rather than refusing the request itself.
    /// </summary>
    public bool IsTransient { get; }
}
