using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using LoginApp.Api.Features.GroceryList;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.Images;
using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Infrastructure.Telemetry;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using SkiaSharp;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// AI pictures end to end: an added item waits for its picture, the worker draws it with the stand-in for Gemini,
/// and every phone receives the item with its picture.
/// </summary>
/// <remarks>
/// The tests share one host, database and generator. Pictures are shared by name, so each test uses names of its own;
/// a test that turns pictures off or changes the generator's answer puts it back.
/// </remarks>
/// <param name="factory">The shared in-memory API host, with pictures enabled.</param>
public sealed class GroceryItemImagesTests(PictureApiFactory factory) : IClassFixture<PictureApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddingAnItem_WaitsForItsPicture_ThenEveryoneGetsTheItemWithIt()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await using RealtimeListener bensPhone = new(factory, ben.AccessToken);
        await bensPhone.StartAsync();
        string name = UniqueName("Kiwi");

        // Act
        GroceryItemResponse added = await ada.AddItemAsync(name);
        GroceryItemResponse drawn = await bensPhone.WaitForSavedAsync(added.Id, item => !item.ImagePending);

        // Assert
        Assert.True(added.ImagePending);
        Assert.Null(added.ImageUrl);
        Assert.StartsWith("/api/grocery-list/images/", drawn.ImageUrl, StringComparison.Ordinal);
        Assert.Equal(added.Version + 1, drawn.Version);
        Assert.Equal(added.LastModifiedAt, drawn.LastModifiedAt);
        Assert.Equal(ada.UserId, drawn.LastModifiedById);
        Assert.Equal(1, factory.Generator.RequestsFor(name));
    }

    [Fact]
    public async Task APicture_IsASmallSquareWebpThatBrowsersMayKeep()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse item = await AddAndWaitForPictureAsync(ada, UniqueName("Fig"));

        // Act
        using HttpResponseMessage response = await ada.Http.GetAsync(PictureUri(item), CancellationToken);
        byte[] picture = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        CacheControlHeaderValue? cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.False(cacheControl.NoStore);
        Assert.Equal(TimeSpan.FromDays(365), cacheControl.MaxAge);
        Assert.Contains(cacheControl.Extensions, extension => extension.Name == "immutable");
        Assert.NotNull(response.Headers.ETag);
        Assert.InRange(picture.Length, 100, 16 * 1024);
        using SKBitmap bitmap = TestPictures.Decode(picture);
        Assert.Equal((GroceryImageThumbnails.PixelSize, GroceryImageThumbnails.PixelSize), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public async Task APicture_AskedForAgainWithItsETag_IsNotSentAgain()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse item = await AddAndWaitForPictureAsync(ada, UniqueName("Date"));
        using HttpResponseMessage first = await ada.Http.GetAsync(PictureUri(item), CancellationToken);
        using HttpRequestMessage again = new(HttpMethod.Get, PictureUri(item));
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        // Act
        using HttpResponseMessage response = await ada.Http.SendAsync(again, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task AnItemNamedLikeOneThatHasAPicture_GetsThatPictureAtOnce()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        string name = UniqueName("Plum");
        GroceryItemResponse first = await AddAndWaitForPictureAsync(ada, name);

        // Act: same name, other capitalization and spacing.
        GroceryItemResponse second = await ada.AddItemAsync($"  {name.ToUpperInvariant().Replace(" ", "   ", StringComparison.Ordinal)} ");

        // Assert
        Assert.False(second.ImagePending);
        Assert.Equal(first.ImageUrl, second.ImageUrl);
        Assert.Equal(1, factory.Generator.RequestsFor(name));
    }

    [Fact]
    public async Task TwoItemsWaitingForTheSameName_CostOnePicture()
    {
        // Arrange: hold the picture back until both items are waiting for it.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        string name = UniqueName("Pear");
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Generator.Answer = async (itemName, cancellationToken) =>
        {
            await release.Task.WaitAsync(cancellationToken);
            return await FakeImageGenerator.DrawAsync(itemName, cancellationToken);
        };

        try
        {
            // Act
            GroceryItemResponse first = await ada.AddItemAsync(name);
            GroceryItemResponse second = await ada.AddItemAsync(name);
            release.SetResult();
            GroceryItemResponse firstDrawn = await phone.WaitForSavedAsync(first.Id, item => !item.ImagePending);
            GroceryItemResponse secondDrawn = await phone.WaitForSavedAsync(second.Id, item => !item.ImagePending);

            // Assert
            Assert.True(first.ImagePending);
            Assert.True(second.ImagePending);
            Assert.NotNull(firstDrawn.ImageUrl);
            Assert.Equal(firstDrawn.ImageUrl, secondDrawn.ImageUrl);
            Assert.Equal(1, factory.Generator.RequestsFor(name));
        }
        finally
        {
            release.TrySetResult();
            factory.Generator.Answer = FakeImageGenerator.DrawAsync;
        }
    }

    [Fact]
    public async Task RenamingAnItem_DrawsAPictureOfItsNewName()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        GroceryItemResponse item = await AddAndWaitForPictureAsync(ada, UniqueName("Chips"), phone);
        string newName = UniqueName("Chocolate chips");

        // Act
        using HttpResponseMessage response = await ada.PatchItemAsync(item.Id, new { name = newName });
        GroceryItemResponse renamed = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        GroceryItemResponse redrawn = await phone.WaitForSavedAsync(item.Id, saved => saved.Version > renamed.Version);

        // Assert
        Assert.True(renamed.ImagePending);
        Assert.Null(renamed.ImageUrl);
        Assert.False(redrawn.ImagePending);
        Assert.NotNull(redrawn.ImageUrl);
        Assert.NotEqual(item.ImageUrl, redrawn.ImageUrl);
        Assert.Equal(1, factory.Generator.RequestsFor(newName));
    }

    [Fact]
    public async Task FixingTheCapitalizationOfAName_KeepsItsPicture()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse item = await AddAndWaitForPictureAsync(ada, UniqueName("oat milk"));

        // Act
        using HttpResponseMessage response = await ada.PatchItemAsync(item.Id, new { name = item.Name.ToUpperInvariant() });
        GroceryItemResponse renamed = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);

        // Assert
        Assert.False(renamed.ImagePending);
        Assert.Equal(item.ImageUrl, renamed.ImageUrl);
    }

    [Fact]
    public async Task ChangingAnythingButTheName_KeepsThePicture()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse item = await AddAndWaitForPictureAsync(ada, UniqueName("Rice"));

        // Act
        using HttpResponseMessage response = await ada.PatchItemAsync(item.Id, new { quantity = "2 kg", store = "Costco" });
        GroceryItemResponse changed = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);

        // Assert
        Assert.False(changed.ImagePending);
        Assert.Equal(item.ImageUrl, changed.ImageUrl);
    }

    [Fact]
    public async Task AFailedPicture_LeavesTheItemWithoutOne()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        string name = UniqueName("Durian");
        factory.Generator.Answer = (_, _) => throw new ImageGenerationException("Gemini drew no picture (reason IMAGE_SAFETY)", isTransient: false);

        try
        {
            // Act
            GroceryItemResponse item = await ada.AddItemAsync(name);
            GroceryItemResponse finished = await phone.WaitForSavedAsync(item.Id, saved => !saved.ImagePending);

            // Assert: not asked again, because Gemini refused rather than failed.
            Assert.Null(finished.ImageUrl);
            Assert.Equal(1, factory.Generator.RequestsFor(name));
        }
        finally
        {
            factory.Generator.Answer = FakeImageGenerator.DrawAsync;
        }
    }

    [Fact]
    public async Task Pictures_AreCountedByOutcome_AndDrawingIsTimed()
    {
        // Arrange
        IMeterFactory meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        using MetricCollector<long> pictures = new(meterFactory, ApplicationMeter.Name, "nibbles.pictures");
        using MetricCollector<double> durations = new(meterFactory, ApplicationMeter.Name, "nibbles.pictures.duration");
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);

        // Act: one picture drawn, one refused.
        await AddAndWaitForPictureAsync(ada, UniqueName("Quince"), phone);
        factory.Generator.Answer = (_, _) => throw new ImageGenerationException("Gemini drew no picture (reason IMAGE_SAFETY)", isTransient: false);
        try
        {
            GroceryItemResponse refused = await ada.AddItemAsync(UniqueName("Rambutan"));
            await phone.WaitForSavedAsync(refused.Id, saved => !saved.ImagePending);
        }
        finally
        {
            factory.Generator.Answer = FakeImageGenerator.DrawAsync;
        }

        // Assert: both outcomes counted, the drawing timed. (Another test's picture may finish meanwhile, so the
        // counts are lower bounds.)
        string?[] outcomes = [.. pictures.GetMeasurementSnapshot().Select(measurement => measurement.Tags[GroceryImageMetrics.OutcomeTag] as string)];
        Assert.Contains(GroceryImageMetrics.Drawn, outcomes);
        Assert.Contains(GroceryImageMetrics.Failed, outcomes);
        Assert.All(durations.GetMeasurementSnapshot(), measurement => Assert.True(measurement.Value >= 0));
        Assert.NotEmpty(durations.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task ATemporaryFailure_IsAskedAgain()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        string name = UniqueName("Lychee");
        int calls = 0;
        factory.Generator.Answer = (itemName, cancellationToken) => Interlocked.Increment(ref calls) == 1
            ? throw new ImageGenerationException("Gemini answered 503 UNAVAILABLE", isTransient: true)
            : FakeImageGenerator.DrawAsync(itemName, cancellationToken);

        try
        {
            // Act
            GroceryItemResponse item = await ada.AddItemAsync(name);
            GroceryItemResponse finished = await phone.WaitForSavedAsync(item.Id, saved => !saved.ImagePending);

            // Assert
            Assert.NotNull(finished.ImageUrl);
            Assert.Equal(2, factory.Generator.RequestsFor(name));
        }
        finally
        {
            factory.Generator.Answer = FakeImageGenerator.DrawAsync;
        }
    }

    [Fact]
    public async Task TurningPicturesOff_StopsWaitingItems_AndNewItemsGetNone()
    {
        // Arrange: hold the picture back once drawing has begun, so the item is still waiting when pictures go off.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        string waitingName = UniqueName("Basil");
        TaskCompletionSource drawing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Generator.Answer = async (itemName, cancellationToken) =>
        {
            drawing.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await FakeImageGenerator.DrawAsync(itemName, cancellationToken);
        };

        try
        {
            GroceryItemResponse waiting = await ada.AddItemAsync(waitingName);
            await drawing.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

            // Act
            GroceryListSettingsResponse off = await ada.SetImageGenerationAsync(enabled: false);
            GroceryListSettingsResponse received = await phone.NextSettingsAsync();
            GroceryItemResponse stopped = await phone.WaitForSavedAsync(waiting.Id, item => !item.ImagePending);
            GroceryItemResponse addedWhileOff = await ada.AddItemAsync(UniqueName("Mint"));

            // The drawing that had already started finishes, but no longer goes to the item.
            release.SetResult();
            await WaitForPictureOfAsync(waitingName);
            GroceryItemResponse afterwards = Assert.Single((await ada.GetListAsync()).Items, item => item.Id == waiting.Id);

            // Assert
            Assert.True(waiting.ImagePending);
            Assert.False(off.ImageGenerationEnabled);
            Assert.Equal(off, received);
            Assert.Null(stopped.ImageUrl);
            Assert.False(addedWhileOff.ImagePending);
            Assert.Null(addedWhileOff.ImageUrl);
            Assert.Null(afterwards.ImageUrl);
            Assert.False(afterwards.ImagePending);
        }
        finally
        {
            release.TrySetResult();
            factory.Generator.Answer = FakeImageGenerator.DrawAsync;
            await ada.SetImageGenerationAsync(enabled: true);
        }
    }

    [Fact]
    public async Task ChangingTheSettings_ReachesEveryoneAndIsKept()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper ben = await Shopper.SignUpAsync(factory, "Ben");
        await using RealtimeListener bensPhone = await ListenAsync(ben);
        GroceryListSettingsResponse before = (await ada.GetListAsync()).Settings;

        try
        {
            // Act
            GroceryListSettingsResponse off = await ada.SetImageGenerationAsync(enabled: false);
            GroceryListSettingsResponse received = await bensPhone.NextSettingsAsync();
            GroceryListSettingsResponse reread = (await ben.GetListAsync()).Settings;

            // Assert
            Assert.True(before.ImageGenerationEnabled);
            Assert.True(before.ImageGenerationAvailable);
            Assert.False(off.ImageGenerationEnabled);
            Assert.True(off.ImageGenerationAvailable);
            Assert.Equal(before.Version + 1, off.Version);
            Assert.Equal(off, received);
            Assert.Equal(off, reread);
        }
        finally
        {
            await ada.SetImageGenerationAsync(enabled: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"imageGenerationEnabled\":true}")]
    public async Task SettingsThatDoNotChange_AreNotSavedAgain(string body)
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryListSettingsResponse before = (await ada.GetListAsync()).Settings;
        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");

        // Act
        using HttpResponseMessage response = await ada.Http.PatchAsync(new Uri("/api/grocery-list/settings", UriKind.Relative), content, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, (await ada.GetListAsync()).Settings);
    }

    [Fact]
    public async Task ItemsLeftWaitingByARestart_GetTheirPictures()
    {
        // Arrange: an item saved as waiting, the way a restart in mid-drawing leaves it, with no worker told about it.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        await using RealtimeListener phone = await ListenAsync(ada);
        var itemId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.GroceryItems.Add(new GroceryItem
            {
                Id = itemId,
                Name = UniqueName("Leek"),
                Position = -1_000_000,
                Version = 1,
                CreatedAt = now,
                LastModifiedAt = now,
                LastModifiedById = ada.UserId,
                ImagePending = true,
            });
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        // Act: the worker looks for waiting items when it starts and whenever it is woken; here, it is woken.
        factory.Services.GetRequiredService<GroceryImageSignal>().Notify();
        GroceryItemResponse drawn = await phone.WaitForSavedAsync(itemId, item => !item.ImagePending);

        // Assert
        Assert.NotNull(drawn.ImageUrl);
    }

    [Fact]
    public async Task APictureThatDoesNotExist_IsNotFound()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");

        // Act
        using HttpResponseMessage response = await ada.Http.GetAsync(
            new Uri($"/api/grocery-list/images/{Guid.NewGuid()}", UriKind.Relative),
            CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static string UniqueName(string name) => $"{name} {Guid.NewGuid().ToString("N")[..8]}";

    private static Uri PictureUri(GroceryItemResponse item) =>
        new(item.ImageUrl ?? throw new InvalidOperationException("The item has no picture."), UriKind.Relative);

    private async Task<RealtimeListener> ListenAsync(Shopper shopper)
    {
        RealtimeListener listener = new(factory, shopper.AccessToken);
        await listener.StartAsync();
        return listener;
    }

    private async Task<GroceryItemResponse> AddAndWaitForPictureAsync(Shopper shopper, string name, RealtimeListener? listener = null)
    {
        await using RealtimeListener? ownListener = listener is null ? await ListenAsync(shopper) : null;
        GroceryItemResponse added = await shopper.AddItemAsync(name);
        GroceryItemResponse drawn = await (listener ?? ownListener!).WaitForSavedAsync(added.Id, item => !item.ImagePending);
        Assert.NotNull(drawn.ImageUrl);
        return drawn;
    }

    // Waits until the worker has saved a picture for the name, whether or not an item got it.
    private async Task WaitForPictureOfAsync(string name)
    {
        string nameKey = GroceryImageNames.KeyFor(name);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await dbContext.GroceryItemImages.AnyAsync(image => image.NameKey == nameKey, timeout.Token))
            {
                // The worker saves the picture and then decides about the item; give it a moment for the latter.
                await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
