using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyPendingEncounterTests
{
    private static Party CreateParty() =>
        Party.Create(Guid.NewGuid(), "Heroes", [new PartyMember("Conan", CharacterClass.Warrior)], DateTimeOffset.UtcNow, "ABCD-EFGH");

    private static PendingEncounter Pending(string prompt = "p") => new(RoomType.Combat, prompt, ["a", "b", "c"]);

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
