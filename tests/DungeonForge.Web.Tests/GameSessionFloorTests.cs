using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Lobby;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class GameSessionFloorTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private static PartyResponse Party() =>
        new(Id, "Heroes", [new MemberDto("A", CharacterClass.Warrior, 30, 30, false)], DateTimeOffset.UtcNow);

    private static ExploreResponse BossRound(int turn, int floor, int bossHp, int bossMaxHp, bool cleared = false) =>
        new(Id, turn, RoomType.Boss, null, 0, 0, "x", 0, false, Floor: floor, BossHp: bossHp, BossMaxHp: bossMaxHp, FloorCleared: cleared);

    [Fact]
    public void NewSession_IsOnFloorOne_WithNoBoss()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Floor.Should().Be(1);
        sut.BossMaxHp.Should().Be(0);
    }

    [Fact]
    public void Apply_BossRound_TracksBossHp()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Apply(BossRound(5, floor: 1, bossHp: 20, bossMaxHp: 30));

        sut.Floor.Should().Be(1);
        sut.BossHp.Should().Be(20);
        sut.BossMaxHp.Should().Be(30);
    }

    [Fact]
    public void Apply_FloorCleared_MovesToTheNextFloorAndDropsTheBoss()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(BossRound(5, floor: 1, bossHp: 20, bossMaxHp: 30));

        sut.Apply(BossRound(6, floor: 2, bossHp: 0, bossMaxHp: 0, cleared: true));

        sut.Floor.Should().Be(2);
        sut.BossMaxHp.Should().Be(0);
    }

    [Fact]
    public void ApplyPartyState_NewerTurn_RefreshesFloorAndBoss()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyPartyState(new PartyStateDto(Id, "Heroes", sut.Members, DateTimeOffset.UtcNow, 0, 4, false,
            Floor: 1, RoomsCleared: 4, BossHp: 30, BossMaxHp: 30)).Should().BeTrue();

        sut.BossHp.Should().Be(30);
        sut.BossMaxHp.Should().Be(30);
    }

    [Fact]
    public void Clear_ResetsFloorAndBoss()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(BossRound(5, floor: 3, bossHp: 20, bossMaxHp: 50));

        sut.Clear();

        sut.Floor.Should().Be(1);
        sut.BossMaxHp.Should().Be(0);
    }
}
