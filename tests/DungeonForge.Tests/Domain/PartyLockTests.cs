using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyLockTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static Party CreateParty(int members = 2)
    {
        var party = Party.Create(
            Guid.NewGuid(), "Heroes",
            Enumerable.Range(0, members).Select(i => new PartyMember($"M{i}", CharacterClass.Warrior)).ToList(),
            DateTimeOffset.UtcNow);
        party.CompleteScenario(new PendingEncounter(RoomType.Puzzle, "p"));
        return party;
    }

    [Fact]
    public void TryLockIn_WithoutPending_ReturnsFalse()
    {
        var party = Party.Create(Guid.NewGuid(), "H", [new PartyMember("M0", CharacterClass.Mage)], DateTimeOffset.UtcNow);

        party.TryLockIn(new PlayerAction(A, "M0", "x")).Should().BeFalse();
        party.AllLockedIn.Should().BeFalse();
    }

    [Fact]
    public void AllLockedIn_RequiresDistinctMemberIds()
    {
        var party = CreateParty();

        party.TryLockIn(new PlayerAction(A, "M0", "one"));
        party.TryLockIn(new PlayerAction(A, "M0", "again"));
        party.AllLockedIn.Should().BeFalse();

        party.TryLockIn(new PlayerAction(B, "M1", "two"));
        party.AllLockedIn.Should().BeTrue();
    }

    [Fact]
    public void ReLock_ReplacesTheActionText()
    {
        var party = CreateParty();
        party.TryLockIn(new PlayerAction(A, "M0", "old"));

        party.TryLockIn(new PlayerAction(A, "M0", "new"));

        party.Pending!.Discussion!.Select(a => a.Text).Should().Equal("new");
        party.LockedCount.Should().Be(1);
    }

    [Fact]
    public void Unlock_RemovesTheLock()
    {
        var party = CreateParty();
        party.TryLockIn(new PlayerAction(A, "M0", "x"));

        party.Unlock(A);

        party.LockedCount.Should().Be(0);
    }

    [Fact]
    public void AdvanceTurn_ClearsLocks()
    {
        var party = CreateParty();
        party.TryLockIn(new PlayerAction(A, "M0", "x"));

        party.AdvanceTurn();

        party.LockedCount.Should().Be(0);
    }

    [Fact]
    public void CompleteScenario_ClearsLocks()
    {
        var party = CreateParty();
        party.TryLockIn(new PlayerAction(A, "M0", "x"));

        party.CompleteScenario(new PendingEncounter(RoomType.Combat, "next"));

        party.LockedCount.Should().Be(0);
    }

    [Fact]
    public void DefeatedMembers_DoNotNeedToLockIn()
    {
        var party = CreateParty();
        party.Members[1].TakeDamage(1000);
        party.TryLockIn(new PlayerAction(A, "M0", "x"));

        party.AllLockedIn.Should().BeTrue();
    }

    [Fact]
    public void LockStatus_MatchesLockedActionsToMembersByName_ThenFillsRemainingSlots()
    {
        var party = CreateParty(3);
        party.TryLockIn(new PlayerAction(A, "M1", "x"));
        party.TryLockIn(new PlayerAction(B, "Stranger", "y"));

        party.LockStatus.Select(l => (l.Name, l.LockedIn)).Should().Equal(("M0", true), ("M1", true), ("M2", false));
    }
}
