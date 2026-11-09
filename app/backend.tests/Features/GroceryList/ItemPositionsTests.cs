using LoginApp.Api.Features.GroceryList;

namespace LoginApp.Api.Tests.Features.GroceryList;

/// <summary>
/// The fractional positions that order the list.
/// </summary>
public sealed class ItemPositionsTests
{
    [Fact]
    public void Between_InAnEmptyList_StartsAtZero() =>
        Assert.Equal(0, ItemPositions.Between(before: null, after: null));

    [Fact]
    public void Between_AtEitherEnd_StepsOutBySpacing()
    {
        Assert.Equal(-ItemPositions.Spacing, ItemPositions.Between(before: null, after: 0));
        Assert.Equal(5 + ItemPositions.Spacing, ItemPositions.Between(before: 5, after: null));
    }

    [Fact]
    public void Between_TwoNeighbors_ReturnsTheMidpoint() =>
        Assert.Equal(1.5, ItemPositions.Between(before: 1, after: 2));

    [Fact]
    public void Between_RepeatedlyHalvedGap_EventuallyAsksForRenumbering()
    {
        // Arrange
        double before = 0;
        double after = ItemPositions.Spacing;
        int splits = 0;

        // Act
        while (ItemPositions.Between(before, after) is double position)
        {
            Assert.InRange(position, before, after);
            Assert.True(position > before && position < after);
            after = position;
            splits++;
        }

        // Assert: plenty of moves fit before a renumbering is needed, and the gap never collapses.
        Assert.InRange(splits, 25, 40);
        Assert.True(after > before);
    }

    [Theory]
    [InlineData(2, 1)] // neighbors out of order
    [InlineData(1, 1)] // no gap at all
    public void Between_WithoutRoom_AsksForRenumbering(double before, double after) =>
        Assert.Null(ItemPositions.Between(before, after));

    [Fact]
    public void Between_FarFromZero_AsksForRenumberingBeforePrecisionSuffers() =>
        Assert.Null(ItemPositions.Between(before: null, after: -1e9));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, ItemPositions.Spacing)]
    [InlineData(2, 2 * ItemPositions.Spacing)]
    public void AtIndex_SpacesItemsEvenly(int index, double expected) =>
        Assert.Equal(expected, ItemPositions.AtIndex(index));
}
