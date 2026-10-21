using FluentAssertions;
using static DungeonForge.Web.Tests.SessionData;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class GameSessionSyncTests
{
    [Fact]
    public void ApplyPartyState_NewerTurn_ReplacesStateAndRaisesChanged()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyPartyState(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 12, 3, false));

        applied.Should().BeTrue();
        sut.Turn.Should().Be(3);
        sut.Gold.Should().Be(12);
        sut.Members[0].Hp.Should().Be(7);
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPartyState_StaleOrEqualTurnOrOtherParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        var stale = new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 1, 30)], DateTimeOffset.UtcNow, 0, 1, false);

        sut.ApplyPartyState(stale).Should().BeFalse();
        sut.ApplyPartyState(stale with { Turn = 2 }).Should().BeFalse();
        sut.ApplyPartyState(stale with { Turn = 9, PartyId = Guid.NewGuid() }).Should().BeFalse();

        sut.Turn.Should().Be(2);
        sut.Gold.Should().Be(5);
        sut.Members[0].Hp.Should().Be(30);
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
    public void ApplyPartyState_SameTurnWithNewMember_AddsThemWithoutTouchingTheScene()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));
        var changed = 0;
        sut.Changed += () => changed++;

        var applied = sut.ApplyPartyState(new PartyStateDto(Id, "Heroes",
            [Member("A", CharacterClass.Warrior, 30, 30), Member("B", CharacterClass.Mage, 16, 16), Member("C", CharacterClass.Bard, 20, 20)],
            DateTimeOffset.UtcNow, 0, 0, false));

        applied.Should().BeTrue();
        sut.Members.Select(m => m.Name).Should().Equal("A", "B", "C");
        sut.HasScenario.Should().BeTrue();
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPartyState_Newer_ClearsScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));
        sut.ApplyPartyState(new PartyStateDto(Id, "Heroes", [Member("A", CharacterClass.Warrior, 7, 30)], DateTimeOffset.UtcNow, 1, 1, false));
        sut.HasScenario.Should().BeFalse();
    }
}
