using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Domain.Rules;

// Roller script, per living member in roster order: Pick(others) for the subject, then Pick(100) for "tell a real theft?"
// (>= 70 and the subject really pocketed gold), then Pick(kinds) for the claim, then Pick(templates) for the wording.
public class GaslightingTests
{
    private static readonly PartyMember[] Pair = [new("Conan", CharacterClass.Warrior), new("Merlin", CharacterClass.Mage)];

    [Fact]
    public void EveryLivingMember_GetsOneClaim_AboutSomeoneElse()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(Chaos: true));

        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 1, 2, 3));

        lies.Select(l => l.Target).Should().BeEquivalentTo(party.Members.Where(m => !m.IsDefeated).Select(m => m.Name));
        foreach (var lie in lies)
        {
            lie.Text.Should().NotBeNullOrWhiteSpace();
            lie.Text.Should().NotContain("{");
        }
    }

    [Fact]
    public void ALoneSurvivor_HasNobodyToGaslight_AndGetsNothing()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(Chaos: true), new PartyMember("Conan", CharacterClass.Warrior));

        Gaslighting.Generate(party, new ScriptedRoller(0)).Should().BeEmpty();
    }

    [Fact]
    public void ARealTheft_IsToldTruthfully_WhenTheRollSkipsTheLie()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(Chaos: true), Pair);
        party.DropVault(20, null);
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(99)); // unseen: Conan now holds 20 gold
        party.PocketedGold("Conan").Should().Be(20);

        // Conan hears an invented wound about Merlin; Merlin is told Conan's real theft.
        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 0, 1, 0, 0, 99, 0));

        lies.Should().Equal(
            new Gaslight("Conan", "Merlin is hurt far worse than they let on. They will be a liability by the next room."),
            new Gaslight("Merlin", "I watched Conan slip 20 gold into their coat when the spoils were split. Say nothing. Watch."));
    }

    [Fact]
    public void WithNoRealTheft_AHighRollStillFallsBackToAnInventedClaim()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(Chaos: true), Pair);

        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 99, 1, 0, 0, 99, 1, 0));

        lies.Should().OnlyContain(l => l.Text.Contains("hurt far worse") || l.Text.Contains("festering wound"));
    }

    [Fact]
    public void TheTraitorIsNeverTheOneFramed_WhenTheyAreTheSubject()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true), Pair);
        party.TryAssignTraitor(new ScriptedRoller(1)).Should().BeTrue();
        party.TraitorName.Should().Be("Merlin");

        // Both claims are framings (the fourth kind exists only with a pact).
        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 0, 3, 0, 0, 0, 3, 0));

        lies.Should().HaveCount(2);
        lies.Should().OnlyContain(l => l.Text.Contains("is the Traitor") || l.Text.Contains("true face"));
        lies.Should().OnlyContain(l => !l.Text.Contains("Merlin"), "the real traitor is never named");
        lies[0].Text.Should().Be("The dungeon whispers that Conan is the Traitor. Do not turn your back on them.");
    }

    [Fact]
    public void AFramedInnocent_IsNamedAsTheyAre()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true), Pair);
        party.TryAssignTraitor(new ScriptedRoller(0)).Should().BeTrue();
        party.TraitorName.Should().Be("Conan");

        var lies = Gaslighting.Generate(party, new ScriptedRoller(0, 0, 3, 0, 0, 0, 3, 0));

        lies[0].Text.Should().Be("The dungeon whispers that Merlin is the Traitor. Do not turn your back on them.");
        lies.Should().OnlyContain(l => !l.Text.Contains("Conan"));
    }
}
