using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyPendingEncounterTests
{
    private static Party CreateParty() =>
        Party.Create(Guid.NewGuid(), "Heroes", [new PartyMember("Conan", CharacterClass.Warrior)], DateTimeOffset.UtcNow, "ABCD-EFGH");

    private static PendingEncounter Pending(string prompt = "p") => new(RoomType.Combat, prompt);

    [Fact]
    public void NewParty_HasNoPendingEncounter() => CreateParty().Pending.Should().BeNull();

    [Fact]
    public void TrySetPending_WhenEmpty_StoresAndReturnsIt()
    {
        var party = CreateParty();
        var pending = Pending();

        var stored = party.TrySetPending(pending);

        stored.Should().BeSameAs(pending);
        party.Pending.Should().BeSameAs(pending);
    }

    [Fact]
    public void TrySetPending_WhenAlreadySet_KeepsFirstAndReturnsIt()
    {
        var party = CreateParty();
        var first = Pending("first");
        party.TrySetPending(first);

        var stored = party.TrySetPending(Pending("second"));

        stored.Should().BeSameAs(first);
        party.Pending.Should().BeSameAs(first);
    }

    [Fact]
    public void ApplyChoiceOutcome_AppliesDamageAndGold_AdvancesTurn_ClearsPending()
    {
        var party = CreateParty();
        party.TrySetPending(Pending());

        var hit = party.ApplyChoiceOutcome(damage: 3, gold: 7);

        hit.Should().NotBeNull();
        hit!.TargetName.Should().Be("Conan");
        hit.Damage.Should().Be(3);
        party.Gold.Should().Be(7);
        party.Turn.Should().Be(1);
        party.Pending.Should().BeNull();
        party.Members[0].Hp.Should().Be(party.Members[0].MaxHp - 3);
    }

    [Fact]
    public void ApplyChoiceOutcome_ZeroDamage_ReturnsNullAndStillAdvances()
    {
        var party = CreateParty();
        party.TrySetPending(Pending());

        var hit = party.ApplyChoiceOutcome(damage: 0, gold: 0);

        hit.Should().BeNull();
        party.Turn.Should().Be(1);
        party.Pending.Should().BeNull();
    }
}

public class PartyScenarioLifecycleTests
{
    private static Party CreateParty() =>
        Party.Create(Guid.NewGuid(), "Heroes", [new PartyMember("Conan", CharacterClass.Warrior)], DateTimeOffset.UtcNow, "ABCD-EFGH");

    [Fact]
    public void TryBeginScenario_WhenIdle_MarksAwaitingAndReturnsTrue()
    {
        var party = CreateParty();

        party.TryBeginScenario(RoomType.Chest).Should().BeTrue();

        party.AwaitingRoom.Should().Be(RoomType.Chest);
        party.Pending.Should().BeNull();
    }

    [Fact]
    public void TryBeginScenario_WhenAlreadyAwaitingOrPending_ReturnsFalse()
    {
        var awaiting = CreateParty();
        awaiting.TryBeginScenario(RoomType.Chest);
        var pending = CreateParty();
        pending.CompleteScenario(new PendingEncounter(RoomType.Combat, "p"));

        awaiting.TryBeginScenario(RoomType.Puzzle).Should().BeFalse();
        pending.TryBeginScenario(RoomType.Puzzle).Should().BeFalse();
        awaiting.AwaitingRoom.Should().Be(RoomType.Chest);
    }

    [Fact]
    public void CompleteScenario_StoresPending_AndClearsAwaiting()
    {
        var party = CreateParty();
        party.TryBeginScenario(RoomType.Puzzle);
        var encounter = new PendingEncounter(RoomType.Puzzle, "A riddle. What do you do?");

        party.CompleteScenario(encounter);

        party.Pending.Should().BeSameAs(encounter);
        party.AwaitingRoom.Should().BeNull();
    }

    [Fact]
    public void AbortScenario_ClearsAwaiting_SoANewScenarioCanBegin()
    {
        var party = CreateParty();
        party.TryBeginScenario(RoomType.Puzzle);

        party.AbortScenario();

        party.AwaitingRoom.Should().BeNull();
        party.TryBeginScenario(RoomType.Combat).Should().BeTrue();
    }
}
