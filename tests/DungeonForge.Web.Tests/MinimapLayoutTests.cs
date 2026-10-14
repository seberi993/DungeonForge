using DungeonForge.Web.Components;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class MinimapLayoutTests
{
    [Fact]
    public void Snake_FirstRowLeftToRight_SecondRowRightToLeft()
    {
        MinimapLayout.Position(0, 4).Should().Be(new MinimapPosition(0, 0));
        MinimapLayout.Position(3, 4).Should().Be(new MinimapPosition(0, 3));
        MinimapLayout.Position(4, 4).Should().Be(new MinimapPosition(1, 3));
        MinimapLayout.Position(7, 4).Should().Be(new MinimapPosition(1, 0));
        MinimapLayout.Position(8, 4).Should().Be(new MinimapPosition(2, 0));
    }

    [Fact]
    public void Positions_AreDeterministicAndNonOverlapping()
    {
        var a = MinimapLayout.Layout(23, 5);
        var b = MinimapLayout.Layout(23, 5);

        a.Should().Equal(b);
        a.Should().HaveCount(23).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void ConsecutiveTiles_AreAdjacent()
    {
        var tiles = MinimapLayout.Layout(30, 5);
        for (var i = 1; i < tiles.Count; i++)
            (Math.Abs(tiles[i].Row - tiles[i - 1].Row) + Math.Abs(tiles[i].Col - tiles[i - 1].Col)).Should().Be(1);
    }

    [Fact]
    public void Rows_ComputedFromCount()
    {
        MinimapLayout.RowCount(0, 5).Should().Be(0);
        MinimapLayout.RowCount(5, 5).Should().Be(1);
        MinimapLayout.RowCount(6, 5).Should().Be(2);
    }

    [Fact]
    public void Layout_Empty_IsEmpty() => MinimapLayout.Layout(0, 5).Should().BeEmpty();
}
