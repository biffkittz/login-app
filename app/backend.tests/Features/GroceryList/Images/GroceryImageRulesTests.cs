using LoginApp.Api.Features.GroceryList.Images;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList.Images;

/// <summary>
/// The small rules around pictures: which names share one, and how many may be drawn a day.
/// </summary>
public sealed class GroceryImageRulesTests
{
    [Theory]
    [InlineData("Milk", "MILK")]
    [InlineData("  oat   milk ", "OAT MILK")]
    [InlineData("Oat\tMilk", "OAT MILK")]
    [InlineData("Crème fraîche", "CRÈME FRAÎCHE")]
    public void KeyFor_IgnoresCaseAndSpacing(string name, string expected) =>
        Assert.Equal(expected, GroceryImageNames.KeyFor(name));

    [Fact]
    public void Budget_AllowsTheDailyLimitInAny24Hours()
    {
        // Arrange
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));
        GroceryImageBudget budget = new(clock, Options.Create(new ImageGenerationOptions { MaxImagesPerDay = 2 }));

        // Act + Assert
        Assert.True(budget.TryTake());
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());

        // The first picture leaves the window 24 hours after it was taken, freeing one place.
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());
    }

    [Fact]
    public void Budget_OfZero_AllowsNothing()
    {
        // Arrange
        GroceryImageBudget budget = new(new FakeTimeProvider(), Options.Create(new ImageGenerationOptions { MaxImagesPerDay = 0 }));

        // Act + Assert
        Assert.False(budget.TryTake());
    }
}
