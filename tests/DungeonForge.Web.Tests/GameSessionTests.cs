using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class GameSessionTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static MemberDto Member(string name, CharacterClass cls, int hp, int maxHp) => new(name, cls, hp, maxHp, hp == 0);
    private static PartyResponse Party() => new(Id, "Heroes", [Member("A", CharacterClass.Warrior, 30, 30), Member("B", CharacterClass.Mage, 16, 16)], DateTimeOffset.UtcNow);
    private static ExploreResponse Turn(int turn, RoomType room, string? target, int damage, int gold, int totalGold, bool defeated = false, string flavor = "x") =>
        new(Id, turn, room, target, damage, gold, flavor, totalGold, defeated);

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
        sut.Members.Select(m => m.Hp).Should().Equal(30, 16);
        sut.TotalHp.Should().Be(46);
        sut.TotalMaxHp.Should().Be(46);
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

        sut.Apply(Turn(1, RoomType.Combat, "A", 4, 0, 0, flavor: "Fight"));
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 9, 9, flavor: "Loot"));

        sut.Turn.Should().Be(2);
        sut.Members[0].Hp.Should().Be(26);
        sut.Members[1].Hp.Should().Be(16);
        sut.Gold.Should().Be(9);
        sut.Log.Should().HaveCount(2);
        sut.Log[0].FlavorEvent.Should().Be("Fight");
        sut.Log[1].RoomType.Should().Be(RoomType.Chest);
    }

    [Fact]
    public void Apply_PartyDefeated_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Combat, "A", 20, 0, 0, defeated: true));
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

        var applied = sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5));

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
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyRemote(Turn(2, RoomType.Chest, null, 0, 5, 5)).Should().BeFalse();
        sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5)).Should().BeFalse();

        sut.Log.Should().HaveCount(1);
        changed.Should().Be(0);
    }

    [Fact]
    public void ApplyRemote_OtherParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(Turn(1, RoomType.Chest, null, 0, 5, 5) with { PartyId = Guid.NewGuid() }).Should().BeFalse();
        sut.Log.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRemote_PartyDefeated_MarksDefeated()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyRemote(Turn(1, RoomType.Combat, "A", 20, 0, 0, defeated: true));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Join_LoadsStateFromDto()
    {
        var sut = new GameSession();
        sut.Join(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 30, 4, false));

        sut.HasParty.Should().BeTrue();
        sut.Name.Should().Be("Heroes");
        sut.TotalHp.Should().Be(7);
        sut.Gold.Should().Be(30);
        sut.Turn.Should().Be(4);
        sut.IsDefeated.Should().BeFalse();
        sut.Join(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 0, 30)], DateTimeOffset.UtcNow, 30, 5, true));
        sut.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 5, 5));
        sut.Clear();

        sut.HasParty.Should().BeFalse();
        sut.Log.Should().BeEmpty();
        sut.Gold.Should().Be(0);
    }
}
