using LoginApp.Api.Features.GroceryList.CostEstimates;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LoginApp.Api.Tests.Features.GroceryList.CostEstimates;

/// <summary>
/// The daily allowance of estimates, and how model-written text is made safe to show.
/// </summary>
public sealed class CostEstimateRulesTests
{
    [Fact]
    public void Budget_AllowsTheDailyLimitInAny24Hours()
    {
        // Arrange
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));
        CostEstimateBudget budget = new(clock, Options.Create(new CostEstimateOptions { MaxEstimatesPerDay = 3 }));

        // Act + Assert
        Assert.True(budget.TryTake());
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake());
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());

        // A day after the first, it frees up; the other two still count.
        clock.Advance(TimeSpan.FromHours(12));
        Assert.True(budget.TryTake());
        Assert.False(budget.TryTake());
    }

    [Fact]
    public void Budget_OfZero_AllowsNothing()
    {
        // Arrange
        CostEstimateBudget budget = new(new FakeTimeProvider(), Options.Create(new CostEstimateOptions { MaxEstimatesPerDay = 0 }));

        // Act + Assert
        Assert.False(budget.TryTake());
    }

    [Theory]
    [InlineData("1 gallon, store brand", "1 gallon, store brand")]
    [InlineData("  two\r\nlines\tand  tabs  ", "two lines and tabs")]
    [InlineData("price \u202Ereversed\u202C here", "price reversed here")]
    [InlineData("zero\u200Bwidth\u2028separator", "zerowidth separator")]
    [InlineData("bell\u0007", "bell")]
    [InlineData("   ", null)]
    [InlineData("\u200B\u202E", null)]
    [InlineData(null, null)]
    public void Clean_LeavesOneLineOfPlainText(string? text, string? expected) =>
        Assert.Equal(expected, CostEstimateService.Clean(text, 100));

    [Fact]
    public void Clean_CutsLongText_WithoutSplittingACharacter()
    {
        // Arrange: an emoji (two UTF-16 units) straddling the limit.
        string text = "abcd\U0001F34Efgh";

        // Act + Assert
        Assert.Equal("abcd", CostEstimateService.Clean(text, 5));
        Assert.Equal("abcd\U0001F34E", CostEstimateService.Clean(text, 6));
        Assert.Equal("ab", CostEstimateService.Clean("ab cd", 3));
    }
}
