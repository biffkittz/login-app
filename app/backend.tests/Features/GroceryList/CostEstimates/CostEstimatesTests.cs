using System.Net;
using System.Net.Http.Json;
using LoginApp.Api.Features.GroceryList.Contracts;
using LoginApp.Api.Features.GroceryList.CostEstimates;
using LoginApp.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LoginApp.Api.Tests.Features.GroceryList.CostEstimates;

/// <summary>
/// <c>POST /api/grocery-list/cost-estimate</c> through the real API, with a stand-in for Gemini.
/// </summary>
/// <remarks>
/// Tests in this class share one list, which grows as they run, so each looks up the items it added by ID.
/// </remarks>
/// <param name="factory">The shared in-memory API host.</param>
public sealed class CostEstimatesTests(CostEstimateApiFactory factory) : IClassFixture<CostEstimateApiFactory>
{
    internal const string EstimatePath = "/api/grocery-list/cost-estimate";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Estimate_PricesEveryItemInListOrder_AndAddsThemUp()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse milk = await AddAsync(shopper, new
        {
            id = Guid.NewGuid(),
            name = $"Milk {Guid.NewGuid():N}",
            quantity = "1 gallon",
            brand = "Horizon",
            store = "Costco",
            notes = "For Grandma's visit",
        });
        GroceryItemResponse bread = await shopper.AddItemAsync($"Bread {Guid.NewGuid():N}");

        // Act
        CostEstimateResponse estimate = await EstimateAsync(shopper);

        // Assert: every item, in the list's order, each at the stand-in's price, and the sum.
        GroceryListResponse list = await shopper.GetListAsync();
        Assert.Equal(list.Items.Select(item => item.Id), estimate.Items.Select(line => line.ItemId));
        Assert.All(estimate.Items, line => Assert.Equal(FakeCostEstimator.PricePerItem, line.EstimatedUsd));
        Assert.Equal(FakeCostEstimator.PricePerItem * list.Items.Count, estimate.TotalUsd);
        Assert.Equal(list.Items.Count, estimate.EstimatedItemCount);
        Assert.Equal("National averages.", estimate.Note);

        CostEstimateLineResponse milkLine = estimate.Items.Single(line => line.ItemId == milk.Id);
        Assert.Equal(milk.Name, milkLine.Name);
        Assert.Equal("1 gallon", milkLine.Quantity);
        Assert.Contains(estimate.Items, line => line.ItemId == bread.Id);

        // Only the name, quantity and brand reach the model: not the store, the notes or who added it.
        CostEstimateItem sentMilk = factory.Estimator.Requests.Last().Single(item => item.Name == milk.Name);
        Assert.Equal(new CostEstimateItem(sentMilk.ItemNumber, milk.Name, "1 gallon", "Horizon"), sentMilk);
    }

    [Fact]
    public async Task Estimate_ItemsTheModelSkippedOrPricedImplausibly_HaveNoPrice()
    {
        // Arrange: the model prices the apples twice (the first answer counts) with a messy basis, skips the pears,
        // prices the plums at $50,000, and invents an item that isn't on the list. Everything else costs $1.
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        GroceryItemResponse apples = await shopper.AddItemAsync($"Apples {Guid.NewGuid():N}");
        GroceryItemResponse pears = await shopper.AddItemAsync($"Pears {Guid.NewGuid():N}");
        GroceryItemResponse plums = await shopper.AddItemAsync($"Plums {Guid.NewGuid():N}");
        factory.Estimator.Answer = (items, _) =>
        {
            int Number(GroceryItemResponse item) => items.Single(sent => sent.Name == item.Name).ItemNumber;
            int[] special = [Number(apples), Number(pears), Number(plums)];
            return Task.FromResult(new ModelCostEstimate(
                [
                    new ModelCostLine(Number(apples), 3.125m, "  3 lb\nbag \u202Eof apples "),
                    new ModelCostLine(Number(apples), 99m, "Second answer"),
                    new ModelCostLine(Number(plums), 50_000m, "Diamond plums"),
                    .. items.Where(item => !special.Contains(item.ItemNumber)).Select(item => new ModelCostLine(item.ItemNumber, 1m, null)),
                    new ModelCostLine(items.Count + 1, 7m, "Not on the list"),
                ],
                null));
        };
        try
        {
            // Act
            CostEstimateResponse estimate = await EstimateAsync(shopper);

            // Assert
            CostEstimateLineResponse Line(GroceryItemResponse item) => estimate.Items.Single(line => line.ItemId == item.Id);
            Assert.Equal(3.13m, Line(apples).EstimatedUsd);
            Assert.Equal("3 lb bag of apples", Line(apples).Basis);
            Assert.Null(Line(pears).EstimatedUsd);
            Assert.Null(Line(pears).Basis);
            Assert.Null(Line(plums).EstimatedUsd);
            Assert.Equal("Diamond plums", Line(plums).Basis);

            int others = estimate.Items.Count - 3;
            Assert.Equal(1 + others, estimate.EstimatedItemCount);
            Assert.Equal(3.13m + others, estimate.TotalUsd);
            Assert.Null(estimate.Note);
        }
        finally
        {
            factory.Estimator.Answer = FakeCostEstimator.PriceAsync;
        }
    }

    [Theory]
    [InlineData("refused", HttpStatusCode.BadGateway)]
    [InlineData("busy", HttpStatusCode.ServiceUnavailable)]
    [InlineData("unreachable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("unrelated answer", HttpStatusCode.BadGateway)]
    public async Task Estimate_WhenGeminiFails_SaysWhetherToTryAgainSoon(string failure, HttpStatusCode expected)
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        await shopper.AddItemAsync($"Rice {Guid.NewGuid():N}");
        factory.Estimator.Answer = failure switch
        {
            "refused" => (_, _) => throw new CostEstimateException("Gemini answered 403 PERMISSION_DENIED", isTransient: false),
            "busy" => (_, _) => throw new CostEstimateException("Gemini answered 503 UNAVAILABLE", isTransient: true),
            "unreachable" => (_, _) => throw new HttpRequestException("Name or service not known"),
            _ => (items, _) => Task.FromResult(new ModelCostEstimate([new ModelCostLine(items.Count + 5, 1m, null)], null)),
        };
        try
        {
            // Act
            using HttpResponseMessage response = await shopper.Http.PostAsync(new Uri(EstimatePath, UriKind.Relative), null, CancellationToken);

            // Assert
            Assert.Equal(expected, response.StatusCode);
            ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
            Assert.False(string.IsNullOrWhiteSpace(problem?.Title));
            Assert.DoesNotContain("Gemini answered", problem?.Title, StringComparison.Ordinal);
        }
        finally
        {
            factory.Estimator.Answer = FakeCostEstimator.PriceAsync;
        }
    }

    [Fact]
    public async Task Estimate_WithoutSigningIn_IsRefused()
    {
        // Arrange
        using HttpClient anonymous = factory.CreateBrowserLikeClient();
        int before = factory.Estimator.Requests.Count;

        // Act
        using HttpResponseMessage response = await anonymous.PostAsync(new Uri(EstimatePath, UriKind.Relative), null, CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, factory.Estimator.Requests.Count);
    }

    [Fact]
    public async Task Settings_SayEstimatesAreAvailable()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);

        // Act
        GroceryListSettingsResponse settings = (await shopper.GetListAsync()).Settings;

        // Assert
        Assert.True(settings.CostEstimateAvailable);
    }

    private static async Task<GroceryItemResponse> AddAsync(Shopper shopper, object body)
    {
        using HttpResponseMessage response = await shopper.PostItemAsync(body);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await Shopper.ReadChangeAsync(response)).SavedItems);
    }

    private static async Task<CostEstimateResponse> EstimateAsync(Shopper shopper)
    {
        using HttpResponseMessage response = await shopper.Http.PostAsync(new Uri(EstimatePath, UriKind.Relative), null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CostEstimateResponse>(CancellationToken)
            ?? throw new InvalidOperationException("The response had no body.");
    }
}

/// <summary>
/// The daily allowance and the empty list, on a host that allows two estimates a day.
/// </summary>
/// <param name="factory">An API host with its own, initially empty, list.</param>
public sealed class CostEstimateLimitTests(TwoEstimatesADayApiFactory factory) : IClassFixture<TwoEstimatesADayApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Estimate_RefusesAnEmptyList_ThenStopsAtTheDailyLimit()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        Uri estimate = new(CostEstimatesTests.EstimatePath, UriKind.Relative);

        // Act + Assert: an empty list isn't sent to Gemini, and doesn't count.
        using (HttpResponseMessage empty = await shopper.Http.PostAsync(estimate, null, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        }

        await shopper.AddItemAsync("Eggs");
        using HttpResponseMessage first = await shopper.Http.PostAsync(estimate, null, CancellationToken);
        using HttpResponseMessage second = await shopper.Http.PostAsync(estimate, null, CancellationToken);
        using HttpResponseMessage third = await shopper.Http.PostAsync(estimate, null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        ProblemDetails? problem = await third.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
        Assert.Contains("its 2 cost estimates for today", problem?.Title, StringComparison.Ordinal);
        Assert.Equal(2, factory.Estimator.Requests.Count);
    }
}

/// <summary>
/// A server without a Gemini API key: the button has nothing to call.
/// </summary>
/// <param name="factory">An API host without a Gemini key.</param>
public sealed class CostEstimatesUnavailableTests(LoginApiFactory factory) : IClassFixture<LoginApiFactory>
{
    [Fact]
    public async Task WithoutAnApiKey_EstimatesAreUnavailable()
    {
        // Arrange
        using Shopper shopper = await Shopper.SignUpAsync(factory);
        await shopper.AddItemAsync("Butter");

        // Act
        GroceryListSettingsResponse settings = (await shopper.GetListAsync()).Settings;
        using HttpResponseMessage response = await shopper.Http.PostAsync(
            new Uri(CostEstimatesTests.EstimatePath, UriKind.Relative), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(settings.CostEstimateAvailable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
