using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class GameSessionTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static PartyResponse Party() => new(Id, "Heroes", ["A", "B"], DateTimeOffset.UtcNow);

    [Fact]
    public void New_HasNoParty()
    {
        var sut = new GameSession();
        sut.HasParty.Should().BeFalse();
    }

    [Fact]
    public void Start_SetsPartyAndInitialStats()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.HasParty.Should().BeTrue();
        sut.PartyId.Should().Be(Id);
        sut.Name.Should().Be("Heroes");
        sut.Hp.Should().Be(GameSession.StartingHp);
        sut.Gold.Should().Be(0);
        sut.Turn.Should().Be(0);
        sut.Log.Should().BeEmpty();
        sut.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void Apply_UpdatesStatsAndAppendsLog()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, -4, 0, "Fight", 16, 0));
        sut.Apply(new ExploreResponse(Id, 2, RoomType.Chest, 0, 9, "Loot", 16, 9));

        sut.Turn.Should().Be(2);
        sut.Hp.Should().Be(16);
        sut.Gold.Should().Be(9);
        sut.Log.Should().HaveCount(2);
        sut.Log[0].FlavorEvent.Should().Be("Fight");
        sut.Log[1].RoomType.Should().Be(RoomType.Chest);
    }

    [Fact]
    public void Apply_HpZero_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, -20, 0, "Ouch", 0, 0));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void MarkDefeated_SetsDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.MarkDefeated();
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void ApplyRemote_NewTurn_AppliesAndRaisesChanged()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyRemote(new ExploreResponse(Id, 1, RoomType.Chest, 0, 5, "x", 20, 5));

        applied.Should().BeTrue();
        changed.Should().Be(1);
        sut.Turn.Should().Be(1);
        sut.Gold.Should().Be(5);
        sut.Log.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyRemote_StaleOrDuplicateTurn_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(new ExploreResponse(Id, 2, RoomType.Chest, 0, 5, "x", 20, 5));
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyRemote(new ExploreResponse(Id, 2, RoomType.Chest, 0, 5, "x", 20, 5)).Should().BeFalse();
        sut.ApplyRemote(new ExploreResponse(Id, 1, RoomType.Chest, 0, 5, "x", 20, 5)).Should().BeFalse();

        sut.Log.Should().HaveCount(1);
        changed.Should().Be(0);
    }

    [Fact]
    public void ApplyRemote_OtherParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(new ExploreResponse(Guid.NewGuid(), 1, RoomType.Chest, 0, 5, "x", 20, 5)).Should().BeFalse();
        sut.Log.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRemote_HpZero_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(new ExploreResponse(Id, 1, RoomType.Combat, -20, 0, "Ouch", 0, 0));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Join_LoadsStateFromDto()
    {
        var sut = new GameSession();
        sut.Join(new PartyStateDto(Id, "Heroes", ["A"], DateTimeOffset.UtcNow, 7, 30, 4));

        sut.HasParty.Should().BeTrue();
        sut.Name.Should().Be("Heroes");
        sut.Hp.Should().Be(7);
        sut.Gold.Should().Be(30);
        sut.Turn.Should().Be(4);
        sut.IsDefeated.Should().BeFalse();
        sut.Join(new PartyStateDto(Id, "Heroes", ["A"], DateTimeOffset.UtcNow, 0, 30, 5));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(new ExploreResponse(Id, 1, RoomType.Chest, 0, 5, "x", 20, 5));
        sut.Clear();

        sut.HasParty.Should().BeFalse();
        sut.Log.Should().BeEmpty();
        sut.Gold.Should().Be(0);
    }
}
