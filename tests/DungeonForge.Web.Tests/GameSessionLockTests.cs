using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class GameSessionLockTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static PartyResponse Party() => new(Id, "Heroes",
        [new("A", CharacterClass.Warrior, 30, 30, false), new("B", CharacterClass.Mage, 16, 16, false)], DateTimeOffset.UtcNow);
    private static readonly LockStatusDto[] OneOfTwo = [new("A", true), new("B", false)];

    [Fact]
    public void ApplyLockStatus_StoresAndRaisesChanged()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyLockStatus(Id, OneOfTwo).Should().BeTrue();

        sut.LockStatus.Should().Equal(OneOfTwo);
        sut.LockedCount.Should().Be(1);
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyLockStatus_OtherParty_Ignored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyLockStatus(Guid.NewGuid(), OneOfTwo).Should().BeFalse();
        sut.LockStatus.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ResolvedTurn_ClearsLockStatus()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyLockStatus(Id, OneOfTwo);

        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, null, 0, 0, "x", 0, false)).Should().BeTrue();

        sut.LockStatus.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPrompt_WithLockStatus_StoresIt()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Chest, null, 0, 0, "s", 0, false, Scenario: "s", LockStatus: OneOfTwo)).Should().BeTrue();

        sut.LockStatus.Should().Equal(OneOfTwo);
        sut.PendingRoom.Should().Be(RoomType.Chest);
    }

    [Fact]
    public void ApplyNarration_WithNextRoomType_SetsPendingRoom_AndResolveClearsIt()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, null, 0, 0, "x", 0, false, NarrationPending: true));

        sut.ApplyNarration(Id, 1, "out", "next", RoomType.Puzzle).Should().BeTrue();
        sut.PendingRoom.Should().Be(RoomType.Puzzle);

        sut.Apply(new ExploreResponse(Id, 2, RoomType.Puzzle, null, 0, 0, "y", 0, false));
        sut.PendingRoom.Should().BeNull();
    }

    [Fact]
    public void PlayerClass_IsFirstMembersClass()
    {
        var sut = new GameSession();
        sut.PlayerClass.Should().Be(CharacterClass.Warrior);
        sut.Start(new PartyResponse(Id, "H", [new("Z", CharacterClass.Bard, 1, 1, false)], DateTimeOffset.UtcNow));
        sut.PlayerClass.Should().Be(CharacterClass.Bard);
    }
}
