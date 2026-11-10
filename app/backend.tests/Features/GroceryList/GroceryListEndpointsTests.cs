using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Infrastructure.Data;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// End-to-end behavior of the <c>/api/grocery-list</c> endpoints through the real middleware pipeline.
/// </summary>
/// <remarks>
/// Tests in this class share one database and run one after another, so each test only asserts on the items it
/// created itself, and on their order relative to each other.
/// </remarks>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class GroceryListEndpointsTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/api/grocery-list")]
    [InlineData("POST", "/api/grocery-list/items")]
    [InlineData("PATCH", "/api/grocery-list/items/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1")]
    [InlineData("POST", "/api/grocery-list/items/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1/move")]
    [InlineData("DELETE", "/api/grocery-list/items/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1")]
    [InlineData("GET", "/api/grocery-list/images/7a0b5f43-6f7e-4a59-9f64-0c35d4b2f5d1")]
    [InlineData("PATCH", "/api/grocery-list/settings")]
    public async Task Endpoints_WithoutSignIn_ReturnUnauthorized(string method, string path)
    {
        // Arrange
        using HttpClient client = factory.CreateBrowserLikeClient();
        using HttpRequestMessage request = new(new HttpMethod(method), path)
        {
            Content = method == "GET" || method == "DELETE" ? null : JsonContent.Create(new { name = "Milk" }),
        };

        // Act
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_WithAllDetails_SavesTrimmedItemAtTheTopAndRecordsWhoAddedIt()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        GroceryItemResponse existing = await ada.AddItemAsync("Bread");
        var itemId = Guid.NewGuid();

        // Act
        using HttpResponseMessage response = await ada.PostItemAsync(new
        {
            id = itemId,
            name = "  Milk ",
            quantity = " 2 gallons ",
            brand = "Organic Valley",
            store = "Costco",
            notes = "Whole milk,\nnot skim",
        });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        GroceryListChange change = await Shopper.ReadChangeAsync(response);
        Assert.Empty(change.DeletedItemIds);
        GroceryItemResponse item = Assert.Single(change.SavedItems);
        Assert.Equal(itemId, item.Id);
        Assert.Equal("Milk", item.Name);
        Assert.Equal("2 gallons", item.Quantity);
        Assert.Equal("Organic Valley", item.Brand);
        Assert.Equal("Costco", item.Store);
        Assert.Equal("Whole milk,\nnot skim", item.Notes);
        Assert.Equal(1, item.Version);
        Assert.Equal(item.CreatedAt, item.LastModifiedAt);
        Assert.Equal(ada.UserId, item.LastModifiedById);
        Assert.Equal("Ada", item.LastModifiedByName);

        GroceryListResponse list = await ada.GetListAsync();
        Assert.Equal(item, list.Items[0]);
        Assert.True(IndexOf(list, itemId) < IndexOf(list, existing.Id), "New items go to the top of the list.");
    }

    [Fact]
    public async Task CreateItem_WithoutIdOrOptionalDetails_GeneratesIdAndLeavesDetailsUnset()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        using HttpResponseMessage response = await shopper.PostItemAsync(new { name = "Eggs", brand = "   ", notes = "" });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        GroceryItemResponse item = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Null(item.Quantity);
        Assert.Null(item.Brand);
        Assert.Null(item.Store);
        Assert.Null(item.Notes);
    }

    [Fact]
    public async Task CreateItem_WithInvalidFields_ReturnsValidationProblemForEachField()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        using HttpResponseMessage response = await shopper.PostItemAsync(new
        {
            name = "   ",
            quantity = new string('9', 51),
            brand = "Tab\tbrand",
            store = "Line\nbreak",
            notes = "Notes may use line breaks\nbut not a bell\u0007",
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(["Brand", "Name", "Notes", "Quantity", "Store"], problem.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetList_IncludesTheStoreChoicesInTheirConfiguredOrder()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        GroceryListResponse list = await shopper.GetListAsync();

        // Assert
        Assert.Equal(["Whole Foods", "Indian Grocery", "Teeter/Lion", "Costco"], list.Stores);
    }

    [Fact]
    public async Task WithoutAnApiKey_PicturesAreUnavailable_AndItemsNeverWaitForOne()
    {
        // Arrange: this host has no Gemini API key (LoginApiFactory).
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        GroceryListSettingsResponse settings = (await shopper.GetListAsync()).Settings;
        GroceryItemResponse item = await shopper.AddItemAsync("Cabbage");

        // Assert: the switch is on, as for a new list, but has nothing to switch.
        Assert.True(settings.ImageGenerationEnabled);
        Assert.False(settings.ImageGenerationAvailable);
        Assert.False(item.ImagePending);
        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public async Task CreateItem_WithAStoreInAnyCase_SavesTheConfiguredSpelling()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        using HttpResponseMessage response = await shopper.PostItemAsync(new { name = "Paneer", store = " indian grocery " });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Indian Grocery", Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems).Store);
    }

    [Fact]
    public async Task CreateAndUpdate_WithAStoreThatIsNotOffered_ReturnValidationProblemForStore()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Bagels");

        // Act
        using HttpResponseMessage created = await shopper.PostItemAsync(new { name = "Lox", store = "Aldi" });
        using HttpResponseMessage updated = await shopper.PatchItemAsync(item.Id, new { store = "Aldi" });

        // Assert
        foreach (HttpResponseMessage response in new[] { created, updated })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            HttpValidationProblemDetails? problem =
                await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
            Assert.Equal(["Store"], problem?.Errors.Keys);
        }

        Assert.Null((await shopper.GetListAsync()).Items.Single(existing => existing.Id == item.Id).Store);
    }

    [Fact]
    public async Task UpdateItem_OfAnItemWithAStoreNoLongerOffered_KeepsThatStoreUnlessChanged()
    {
        // Arrange: an item saved before the store choices existed.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Stroopwafels");
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            LoginApp.Api.Features.GroceryList.GroceryItem saved = await dbContext.GroceryItems.FindAsync([item.Id], CancellationToken)
                ?? throw new InvalidOperationException("The item should exist.");
            saved.Store = "Aldi";
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        // Act
        using HttpResponseMessage response = await shopper.PatchItemAsync(item.Id, new { quantity = "2 packs" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryItemResponse updated = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.Equal("Aldi", updated.Store);
        Assert.Equal("2 packs", updated.Quantity);
    }

    [Fact]
    public async Task CreateItem_WithExistingId_ReturnsConflict()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse existing = await shopper.AddItemAsync("Apples");

        // Act
        using HttpResponseMessage response = await shopper.PostItemAsync(new { id = existing.Id, name = "Pears" });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Apples", (await shopper.GetListAsync()).Items.Single(item => item.Id == existing.Id).Name);
    }

    [Fact]
    public async Task CreateItem_AfterAnotherItem_IsPlacedDirectlyBelowIt()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse bottom = await shopper.AddItemAsync("Bottom");
        GroceryItemResponse top = await shopper.AddItemAsync("Top");

        // Act
        GroceryItemResponse middle = await shopper.AddItemAsync("Middle", afterItemId: top.Id);

        // Assert
        GroceryListResponse list = await shopper.GetListAsync();
        Assert.Equal([top.Id, middle.Id, bottom.Id], InListOrder(list, top.Id, middle.Id, bottom.Id));
        Assert.Equal(IndexOf(list, top.Id) + 1, IndexOf(list, middle.Id));
    }

    [Fact]
    public async Task CreateItem_AfterAnItemThatNoLongerExists_GoesToTheTop()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        await shopper.AddItemAsync("Something already on the list");

        // Act
        GroceryItemResponse item = await shopper.AddItemAsync("Restored", afterItemId: Guid.NewGuid());

        // Assert
        Assert.Equal(item.Id, (await shopper.GetListAsync()).Items[0].Id);
    }

    [Fact]
    public async Task CreateItem_WhenListIsFull_ReturnsConflict()
    {
        // Arrange: allow exactly one more item than the shared database already holds.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        int itemCount = (await shopper.GetListAsync()).Items.Count;
        await using WebApplicationFactory<Program> limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("GroceryList:MaxItems", (itemCount + 1).ToString(CultureInfo.InvariantCulture)));
        using Shopper limitedShopper = await Shopper.SignUpAsync(limitedFactory);
        await limitedShopper.AddItemAsync("The last one that fits");

        // Act
        using HttpResponseMessage response = await limitedShopper.PostItemAsync(new { name = "One too many" });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Contains("full", problem?.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateItem_ChangesOnlyTheFieldsSent_AndRecordsTheEditor()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper grace = await Shopper.SignUpAsync(factory, "Grace");
        using HttpResponseMessage created = await ada.PostItemAsync(new { name = "Coffee", brand = "Lavazza", store = "Whole Foods" });
        GroceryItemResponse original = Assert.Single((await Shopper.ReadChangeAsync(created)).SavedItems);

        // Act: Grace changes the quantity and clears the store; name and brand are not sent.
        using HttpResponseMessage response = await grace.PatchItemAsync(original.Id, new { quantity = "2 bags", store = "" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryItemResponse updated = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.Equal("Coffee", updated.Name);
        Assert.Equal("Lavazza", updated.Brand);
        Assert.Equal("2 bags", updated.Quantity);
        Assert.Null(updated.Store);
        Assert.Equal(original.Version + 1, updated.Version);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.True(updated.LastModifiedAt >= original.LastModifiedAt);
        Assert.Equal(grace.UserId, updated.LastModifiedById);
        Assert.Equal("Grace", updated.LastModifiedByName);
    }

    [Fact]
    public async Task UpdateItem_WithUnchangedValues_DoesNotCountAsAChange()
    {
        // Arrange
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper grace = await Shopper.SignUpAsync(factory, "Grace");
        GroceryItemResponse original = await ada.AddItemAsync("Tea");

        // Act
        using HttpResponseMessage response = await grace.PatchItemAsync(original.Id, new { name = " Tea ", quantity = "" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryItemResponse unchanged = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.Equal(original, unchanged);
    }

    [Fact]
    public async Task UpdateItem_WithBlankName_ReturnsValidationProblem()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Rice");

        // Act
        using HttpResponseMessage response = await shopper.PatchItemAsync(item.Id, new { name = " " });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.Equal(["Name"], problem?.Errors.Keys);
    }

    [Fact]
    public async Task UpdateItem_ThatDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        using HttpResponseMessage response = await shopper.PatchItemAsync(Guid.NewGuid(), new { name = "Ghost" });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveItem_ToTheTopAndAfterAnotherItem_ReordersWithoutChangingLastModified()
    {
        // Arrange: the list reads C, B, A (new items go to the top).
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper grace = await Shopper.SignUpAsync(factory, "Grace");
        GroceryItemResponse a = await ada.AddItemAsync("A");
        GroceryItemResponse b = await ada.AddItemAsync("B");
        GroceryItemResponse c = await ada.AddItemAsync("C");

        // Act
        using HttpResponseMessage toTop = await grace.MoveItemAsync(a.Id, afterItemId: null);
        using HttpResponseMessage afterB = await grace.MoveItemAsync(c.Id, afterItemId: b.Id);

        // Assert
        Assert.Equal(HttpStatusCode.OK, toTop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterB.StatusCode);
        GroceryListResponse list = await grace.GetListAsync();
        Assert.Equal([a.Id, b.Id, c.Id], InListOrder(list, a.Id, b.Id, c.Id));
        Assert.Equal(IndexOf(list, b.Id) + 1, IndexOf(list, c.Id));

        GroceryItemResponse movedA = list.Items.Single(item => item.Id == a.Id);
        Assert.Equal(a.Version + 1, movedA.Version);
        Assert.Equal(a.LastModifiedAt, movedA.LastModifiedAt);
        Assert.Equal(ada.UserId, movedA.LastModifiedById);
    }

    [Fact]
    public async Task MoveItem_AfterItself_ChangesNothing()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Butter");

        // Act
        using HttpResponseMessage response = await shopper.MoveItemAsync(item.Id, afterItemId: item.Id);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(item, Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems));
    }

    [Fact]
    public async Task MoveItem_IntoAnotherStore_MovesItAndSavesTheStoreAsOneChangeByTheMover()
    {
        // Arrange: the list reads C, B, A; B is bought at Costco.
        using Shopper ada = await Shopper.SignUpAsync(factory, "Ada");
        using Shopper grace = await Shopper.SignUpAsync(factory, "Grace");
        GroceryItemResponse a = await ada.AddItemAsync("Apricots");
        GroceryItemResponse b = await ada.AddItemAsync("Basmati", store: "Costco");
        GroceryItemResponse c = await ada.AddItemAsync("Cumin");
        await using RealtimeListener adasPhone = new(factory, ada.AccessToken);
        await adasPhone.StartAsync();

        // Act: Grace drags C into the Whole Foods section, just below B. The store's spelling is the server's.
        using HttpResponseMessage response = await grace.MoveItemAsync(c.Id, afterItemId: b.Id, store: "whole foods");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryItemResponse moved = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.Equal("Whole Foods", moved.Store);
        Assert.Equal(c.Version + 1, moved.Version);
        Assert.Equal(grace.UserId, moved.LastModifiedById);
        Assert.True(moved.LastModifiedAt >= c.LastModifiedAt);

        GroceryListResponse list = await grace.GetListAsync();
        Assert.Equal([b.Id, c.Id, a.Id], InListOrder(list, a.Id, b.Id, c.Id));
        // Ada's phone hears of it once, with the new place and the new store together.
        GroceryListChange broadcast = await adasPhone.NextChangeAsync();
        Assert.Equal(moved, Assert.Single(broadcast.SavedItems));
    }

    [Fact]
    public async Task MoveItem_IntoAnotherStoreWithoutChangingPlace_ChangesOnlyTheStore()
    {
        // Arrange: the list reads B, A. Dropping B at the top of an empty section keeps its place in the whole list.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse a = await shopper.AddItemAsync("Almonds", store: "Costco");
        GroceryItemResponse b = await shopper.AddItemAsync("Bananas", store: "Costco");

        // Act: "after itself" means "stay where it is".
        using HttpResponseMessage response = await shopper.MoveItemAsync(b.Id, afterItemId: b.Id, store: "Indian Grocery");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryItemResponse moved = Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
        Assert.Equal("Indian Grocery", moved.Store);
        Assert.Equal(b.Position, moved.Position);
        Assert.Equal(b.Version + 1, moved.Version);
        Assert.Equal([b.Id, a.Id], InListOrder(await shopper.GetListAsync(), a.Id, b.Id));
    }

    [Fact]
    public async Task MoveItem_WithAnEmptyStore_ClearsTheStore()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Chickpeas", store: "Indian Grocery");

        // Act
        using HttpResponseMessage response = await shopper.MoveItemAsync(item.Id, afterItemId: null, store: string.Empty);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems).Store);
    }

    [Fact]
    public async Task MoveItem_IntoAStoreThatIsNotOffered_ReturnsValidationProblemAndMovesNothing()
    {
        // Arrange: the list reads B, A.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse a = await shopper.AddItemAsync("Dates");
        GroceryItemResponse b = await shopper.AddItemAsync("Eggs");

        // Act
        using HttpResponseMessage response = await shopper.MoveItemAsync(b.Id, afterItemId: a.Id, store: "Aldi");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        HttpValidationProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken);
        Assert.Equal(["Store"], problem?.Errors.Keys);
        GroceryListResponse list = await shopper.GetListAsync();
        Assert.Equal([b.Id, a.Id], InListOrder(list, a.Id, b.Id));
        Assert.Equal(b, list.Items.Single(item => item.Id == b.Id));
    }

    [Fact]
    public async Task MoveItem_AfterAnItemThatNoLongerExists_ReturnsConflict()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Cheese");

        // Act
        using HttpResponseMessage response = await shopper.MoveItemAsync(item.Id, afterItemId: Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MoveItem_RepeatedlyIntoTheSameGap_RenumbersAndKeepsTheOrderRight()
    {
        // Arrange: each move halves the gap between `first` and its neighbor below, until there is no room left.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        var items = new GroceryItemResponse[45];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = await shopper.AddItemAsync($"Item {i}");
        }

        GroceryItemResponse first = items[^1];
        List<Guid> expectedOrder = [.. items.Reverse().Select(item => item.Id)];
        bool renumbered = false;

        // Act: move each of the older items, in turn, directly below `first`.
        for (int i = 0; i < items.Length - 1; i++)
        {
            using HttpResponseMessage response = await shopper.MoveItemAsync(items[i].Id, afterItemId: first.Id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            renumbered |= (await Shopper.ReadChangeAsync(response)).SavedItems.Count > 1;

            expectedOrder.Remove(items[i].Id);
            expectedOrder.Insert(1, items[i].Id);
        }

        // Assert
        Assert.True(renumbered, "The gap should have become too small and the list renumbered.");
        GroceryListResponse list = await shopper.GetListAsync();
        Assert.Equal(expectedOrder, InListOrder(list, [.. expectedOrder]));
    }

    [Fact]
    public async Task DeleteItem_RemovesItAndReportsTheDeletion()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Flour");

        // Act
        using HttpResponseMessage response = await shopper.DeleteItemAsync(item.Id);
        using HttpResponseMessage again = await shopper.DeleteItemAsync(item.Id);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        GroceryListChange change = await Shopper.ReadChangeAsync(response);
        Assert.Equal([item.Id], change.DeletedItemIds);
        Assert.Empty(change.SavedItems);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.DoesNotContain((await shopper.GetListAsync()).Items, existing => existing.Id == item.Id);
    }

    [Fact]
    public async Task Write_ByAnAccountThatNoLongerExists_ReturnsUnauthorizedAndDeletesTheCookie()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse item = await shopper.AddItemAsync("Sugar");
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Users.Remove(await dbContext.Users.FindAsync([shopper.UserId], CancellationToken)
                ?? throw new InvalidOperationException("The user should exist."));
            await dbContext.SaveChangesAsync(CancellationToken);
        }

        // Act
        using HttpResponseMessage response = await shopper.PatchItemAsync(item.Id, new { name = "Brown sugar" });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);

        // The family's item survives the account; it just has no known editor any more.
        using Shopper other = await Shopper.SignUpAsync(factory);
        GroceryItemResponse orphan = (await other.GetListAsync()).Items.Single(existing => existing.Id == item.Id);
        Assert.Null(orphan.LastModifiedById);
        Assert.Null(orphan.LastModifiedByName);
    }

    [Fact]
    public async Task ListResponses_AreNeverCached()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        using HttpResponseMessage response = await shopper.Http.GetAsync(new Uri("/api/grocery-list", UriKind.Relative), CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static int IndexOf(GroceryListResponse list, Guid itemId) =>
        list.Items.Select(item => item.Id).ToList().IndexOf(itemId);

    // The given items in the order the list holds them, ignoring every other test's items.
    private static List<Guid> InListOrder(GroceryListResponse list, params Guid[] itemIds) =>
        [.. list.Items.Select(item => item.Id).Where(itemIds.Contains)];
}
