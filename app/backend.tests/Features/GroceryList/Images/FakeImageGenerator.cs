using System.Collections.Concurrent;
using LoginApp.Api.Features.GroceryList.Images;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// Stands in for Gemini: records which names were asked for and answers with a test picture, or whatever a test
/// tells it to.
/// </summary>
internal sealed class FakeImageGenerator : IGroceryImageGenerator
{
    private readonly ConcurrentQueue<string> _requests = new();

    /// <summary>
    /// Gets or sets how the next requests are answered. Tests that change it put the default back when done, because
    /// the factory (and so this instance) is shared by a test class.
    /// </summary>
    public Func<string, CancellationToken, Task<byte[]>> Answer { get; set; } = DrawAsync;

    /// <summary>Gets every name asked for so far, in order.</summary>
    public IReadOnlyCollection<string> Requests => _requests;

    /// <summary>The default answer: a 1024-pixel PNG, like Gemini's.</summary>
    /// <param name="itemName">The name asked for.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>The picture.</returns>
    public static Task<byte[]> DrawAsync(string itemName, CancellationToken cancellationToken) =>
        Task.FromResult(TestPictures.Png());

    /// <summary>Counts the requests for a name.</summary>
    /// <param name="itemName">The name, as the item has it.</param>
    /// <returns>How many pictures of it were asked for.</returns>
    public int RequestsFor(string itemName) => _requests.Count(name => name == itemName);

    /// <inheritdoc />
    public Task<byte[]> GenerateAsync(string itemName, CancellationToken cancellationToken)
    {
        _requests.Enqueue(itemName);
        return Answer(itemName, cancellationToken);
    }
}
