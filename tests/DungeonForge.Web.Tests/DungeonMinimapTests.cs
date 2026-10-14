using Bunit;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class DungeonMinimapTests : TestContext
{
    private static readonly MemberDto[] Members =
        [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 0, 16, true)];

    private static LogEntry Entry(int turn, RoomType room) => new(turn, room, null, 0, 0, "x");

    private IRenderedComponent<DungeonMinimap> Render(IReadOnlyList<LogEntry> log, RoomType? pending = null) =>
        RenderComponent<DungeonMinimap>(p => p.Add(c => c.Log, log).Add(c => c.Members, Members).Add(c => c.PendingRoom, pending));

    [Fact]
    public void RendersOneTilePerVisitedRoom_WithMarkerPerRoomType()
    {
        var cut = Render([Entry(1, RoomType.Combat), Entry(2, RoomType.Chest), Entry(3, RoomType.Puzzle)]);

        cut.FindAll(".minimap .tile").Should().HaveCount(3);
        cut.Find(".tile:nth-child(1) .marker").ClassList.Should().Contain("marker-combat");
        cut.Find(".tile:nth-child(2) .marker").ClassList.Should().Contain("marker-chest");
        cut.Find(".tile:nth-child(3) .marker").ClassList.Should().Contain("marker-puzzle");
    }

    [Fact]
    public void PendingRoom_AddsUnexplored_TileWithoutMarker()
    {
        var cut = Render([Entry(1, RoomType.Combat)], RoomType.Chest);

        var tiles = cut.FindAll(".tile").ToList();
        tiles.Should().HaveCount(2);
        tiles[1].ClassList.Should().Contain("unexplored");
        tiles[1].QuerySelector(".marker").Should().BeNull();
        tiles[0].ClassList.Should().NotContain("unexplored");
    }

    [Fact]
    public void OneTokenPerMember_OnCurrentTile()
    {
        var cut = Render([Entry(1, RoomType.Combat), Entry(2, RoomType.Chest)], RoomType.Puzzle);

        cut.FindAll(".token").Should().HaveCount(2);
        var current = cut.Find(".tile.current");
        current.QuerySelectorAll(".token").Should().HaveCount(2);
        current.QuerySelector(".marker")!.ClassList.Should().Contain("marker-chest");
        cut.FindAll(".token").ToList()[1].ClassList.Should().Contain("defeated");
    }

    [Fact]
    public void CurrentTile_HasPulseClass_AndOthersDoNot()
    {
        var cut = Render([Entry(1, RoomType.Combat), Entry(2, RoomType.Chest)]);

        cut.FindAll(".tile.pulse").Should().ContainSingle();
        cut.Find(".tile.pulse").ClassList.Should().Contain("current");
    }

    [Fact]
    public void NoRooms_RendersEmptyMap_WithoutTokens()
    {
        var cut = Render([]);
        cut.FindAll(".tile").Should().BeEmpty();
        cut.FindAll(".token").Should().BeEmpty();
        cut.Find(".minimap").Should().NotBeNull();
    }

    [Fact]
    public void NoVisitedRooms_ButPending_PlacesTokensOnPendingTile()
    {
        var cut = Render([], RoomType.Combat);
        cut.Find(".tile.unexplored.current").QuerySelectorAll(".token").Should().HaveCount(2);
    }

    [Fact]
    public void Tiles_ArePositionedOnSnakePath()
    {
        var cut = Render(Enumerable.Range(1, MinimapLayout.Columns + 1).Select(i => Entry(i, RoomType.Combat)).ToList());

        var tiles = cut.FindAll(".tile").ToList();
        tiles[0].GetAttribute("style").Should().Contain("grid-row:1").And.Contain("grid-column:1");
        tiles[MinimapLayout.Columns].GetAttribute("style").Should().Contain("grid-row:2").And.Contain($"grid-column:{MinimapLayout.Columns}");
    }
}
