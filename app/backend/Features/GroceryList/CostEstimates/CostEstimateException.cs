namespace LoginApp.Api.Features.GroceryList.CostEstimates;

/// <summary>
/// No usable estimate came back: the API refused the request, or its answer wasn't the JSON the schema asks for.
/// </summary>
/// <remarks>
/// Messages are written to the log, so they say what went wrong without repeating the list or the model's text, and
/// without the API's own error messages, which can quote the request.
/// </remarks>
public sealed class CostEstimateException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CostEstimateException"/> class.</summary>
    public CostEstimateException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CostEstimateException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public CostEstimateException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CostEstimateException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public CostEstimateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CostEstimateException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="isTransient">Whether asking again shortly may well succeed.</param>
    public CostEstimateException(string message, bool isTransient)
        : base(message) => IsTransient = isTransient;

    /// <summary>
    /// Gets a value indicating whether asking again shortly may well succeed: the API was overloaded or rate-limited
    /// us, rather than refusing the request itself.
    /// </summary>
    public bool IsTransient { get; }
}
