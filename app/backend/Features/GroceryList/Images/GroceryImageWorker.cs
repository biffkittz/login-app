using LoginApp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LoginApp.Api.Features.GroceryList.Images;

/// <summary>
/// Draws the pictures that new and renamed items are waiting for (<see cref="GroceryItem.ImagePending"/>) in the
/// background, so adding an item never waits for Gemini.
/// </summary>
/// <remarks>
/// <para>
/// The queue is the database. The worker looks for waiting items when the app starts, which finishes whatever a
/// restart interrupted, and again whenever <see cref="GroceryImageSignal"/> wakes it, until none are left. For each
/// item it takes the picture already drawn for the same name or draws one, and hands the outcome to
/// <see cref="IGroceryListService.FinishImageAsync"/>, which attaches the picture (or stops the wait) and broadcasts
/// the item like any other change.
/// </para>
/// <para>
/// A failed picture isn't tried again later: the item just has none. Retrying on every restart could spend money on
/// a name Gemini refuses to draw, over and over.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates a scope (and so a database context) per item.</param>
/// <param name="signal">Wakes the worker.</param>
/// <param name="budget">The daily limit.</param>
/// <param name="options">The image settings.</param>
/// <param name="timeProvider">The clock, for delays and timing.</param>
/// <param name="logger">The logger.</param>
internal sealed class GroceryImageWorker(
    IServiceScopeFactory scopeFactory,
    GroceryImageSignal signal,
    GroceryImageBudget budget,
    IOptions<ImageGenerationOptions> options,
    TimeProvider timeProvider,
    ILogger<GroceryImageWorker> logger) : BackgroundService
{
    // Pictures drawn at once, each for a different name: enough to keep up when someone adds items in a burst. Two
    // items with one name never draw two pictures; the second takes the first one's picture in the next round.
    private const int MaxConcurrentDrawings = 2;

    // Waiting items read per round, to find the next names to draw.
    private const int RoundSize = 50;

    // Gemini is regularly "overloaded" for a moment at busy times. Two more tries, a few seconds apart, ride that out.
    private const int MaxAttempts = 3;

    private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(2);

    // After an unexpected failure, such as the database being unreachable.
    private static readonly TimeSpan s_recoveryDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            GroceryImageLog.NotConfigured(logger);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrawWaitingImagesAsync(stoppingToken);
                await signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Waiting items stay marked in the database, so nothing is lost by trying again later: after a pause,
                // or as soon as another item starts waiting. Letting the exception escape would stop the whole app
                // (BackgroundService's default), list and all.
                GroceryImageLog.WorkerFailed(logger, s_recoveryDelay, exception);
                using CancellationTokenSource pause = new(s_recoveryDelay, timeProvider);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, pause.Token);
                await signal.WaitAsync(wait.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    // Each item's picture costs a network round trip to Gemini, so a round draws its names side by side.
    private async Task DrawWaitingImagesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            List<WaitingItem> round = await NextRoundAsync(cancellationToken);
            if (round.Count == 0)
            {
                return;
            }

            await Task.WhenAll(round.Select(item => ProcessAsync(item, cancellationToken)));
        }
    }

    // Top of the list first: new items go there, so the picture the user is looking at comes first.
    private async Task<List<WaitingItem>> NextRoundAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var waiting = await dbContext.GroceryItems
            .AsNoTracking()
            .Where(item => item.ImagePending)
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.Name })
            .Take(RoundSize)
            .ToListAsync(cancellationToken);

        return [.. waiting
            .Select(item => new WaitingItem(item.Id, item.Name, GroceryImageNames.KeyFor(item.Name)))
            .DistinctBy(item => item.NameKey)
            .Take(MaxConcurrentDrawings)];
    }

    private async Task ProcessAsync(WaitingItem item, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        ApplicationDbContext dbContext = services.GetRequiredService<ApplicationDbContext>();

        Guid? imageId = await dbContext.GroceryItemImages.FindIdByNameKeyAsync(item.NameKey, cancellationToken);
        if (imageId is null)
        {
            imageId = await DrawAsync(services, dbContext, item, cancellationToken);
        }
        else
        {
            GroceryImageLog.ImageReused(logger, item.ItemId);
        }

        await services.GetRequiredService<IGroceryListService>()
            .FinishImageAsync(item.ItemId, item.NameKey, imageId, cancellationToken);
    }

    // Returns the new picture's ID, or null when none was drawn (and says why in the log).
    private async Task<Guid?> DrawAsync(
        IServiceProvider services,
        ApplicationDbContext dbContext,
        WaitingItem item,
        CancellationToken cancellationToken)
    {
        // Pictures may have been turned off, or the key removed, since the item started waiting.
        bool enabled = await dbContext.GroceryListSettings
            .Select(settings => (bool?)settings.ImageGenerationEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? GroceryListSettings.CreateDefault().ImageGenerationEnabled;
        if (!enabled || !options.Value.IsConfigured)
        {
            return null;
        }

        if (!budget.TryTake())
        {
            GroceryImageLog.DailyLimitReached(logger, item.ItemId, options.Value.MaxImagesPerDay);
            return null;
        }

        long started = timeProvider.GetTimestamp();
        byte[] content;
        try
        {
            byte[] original = await GenerateWithRetriesAsync(services.GetRequiredService<IGroceryImageGenerator>(), item, cancellationToken);
            content = GroceryImageThumbnails.Create(original);
        }
        catch (Exception exception) when (IsExpectedFailure(exception) && !cancellationToken.IsCancellationRequested)
        {
            GroceryImageLog.ImageFailed(logger, item.ItemId, exception.Message);
            return null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            GroceryImageLog.ImageFailedUnexpectedly(logger, item.ItemId, exception);
            return null;
        }

        GroceryItemImage image = new()
        {
            Id = Guid.CreateVersion7(),
            NameKey = item.NameKey,
            Content = content,
            ContentType = GroceryImageThumbnails.ContentType,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        dbContext.GroceryItemImages.Add(image);

        // The picture has been paid for: keep it even if the app is shutting down, so it isn't drawn again on restart.
        await dbContext.SaveChangesAsync(CancellationToken.None);
        long elapsedMilliseconds = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        GroceryImageLog.ImageDrawn(logger, item.ItemId, content.Length, elapsedMilliseconds);
        return image.Id;
    }

    private async Task<byte[]> GenerateWithRetriesAsync(
        IGroceryImageGenerator generator,
        WaitingItem item,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await generator.GenerateAsync(item.Name, cancellationToken);
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransient(exception) && !cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay = s_retryDelay * attempt;
                GroceryImageLog.ImageRetrying(logger, item.ItemId, delay, exception.Message);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    // Gemini overloaded or rate-limiting, the network failing, or a request timing out (HttpClient reports a timeout
    // as a TaskCanceledException that nobody asked for).
    private static bool IsTransient(Exception exception) =>
        exception is ImageGenerationException { IsTransient: true } or HttpRequestException or TaskCanceledException;

    // Failures that come with a message fit for the log; anything else is logged with its stack trace.
    private static bool IsExpectedFailure(Exception exception) =>
        exception is ImageGenerationException or HttpRequestException or TaskCanceledException;

    private sealed record WaitingItem(Guid ItemId, string Name, string NameKey);
}
