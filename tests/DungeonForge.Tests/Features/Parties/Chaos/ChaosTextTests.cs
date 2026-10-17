using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using FluentAssertions;

namespace DungeonForge.Tests.Features.Parties.Chaos;

// Every sentence a chaos-mode player reads, pinned as written. Wording is a product decision: change it here on purpose.
public class ChaosTextTests
{
    private static ConfrontationResolution Resolution(ConfrontationVote outcome, int goldToParty = 0, int damage = 0) => new(
        "Conan", outcome,
        new Dictionary<ConfrontationVote, int>
        {
            [ConfrontationVote.Forgive] = 1, [ConfrontationVote.DemandCut] = 2, [ConfrontationVote.Banish] = 0, [ConfrontationVote.Attack] = 0,
        },
        goldToParty, 0, outcome == ConfrontationVote.Banish, damage);

    [Fact]
    public void BallotCast_CountsVotesWithoutSayingWhoOrWhat() =>
        ChaosText.BallotCast(2, 4).Should().Be("A ballot was cast (2 of 4).");

    [Theory]
    [InlineData(ConfrontationVote.Forgive, 0, 0, "Verdict on Conan: forgiven and keeps the loot. Ballots: Forgive x1, DemandCut x2.")]
    [InlineData(ConfrontationVote.DemandCut, 10, 0, "Verdict on Conan: made to hand 10 gold to the party. Ballots: Forgive x1, DemandCut x2.")]
    [InlineData(ConfrontationVote.Banish, 0, 0, "Verdict on Conan: banished, the loot forfeit. Ballots: Forgive x1, DemandCut x2.")]
    [InlineData(ConfrontationVote.Attack, 0, 8, "Verdict on Conan: attacked for 8 damage, the loot forfeit. Ballots: Forgive x1, DemandCut x2.")]
    public void Verdict_NamesTheOutcomeAndTheTally(ConfrontationVote outcome, int goldToParty, int damage, string expected) =>
        ChaosText.Verdict(Resolution(outcome, goldToParty, damage)).Should().Be(expected);

    [Fact]
    public void Loot_SaysGoldAndAnyArtifact()
    {
        ChaosText.Loot(20, null).Should().Be("20 gold");
        ChaosText.Loot(20, "Cursed Idol").Should().Be("20 gold and a Cursed Idol");
    }

    [Fact]
    public void VaultSentences()
    {
        ChaosText.VaultShared("Conan", "20 gold").Should().Be("Conan declared the vault and shared 20 gold with the party.");
        ChaosText.VaultEmptied.Should().Be("The vault has been emptied. Nobody says a word.");
        ChaosText.PocketedUnseen("20 gold").Should().Be("You pocketed 20 gold and nobody saw.");
        ChaosText.CaughtPocketing("Conan", "20 gold", ["Lyra", "Dru"]).Should().Be(
            "Conan was caught pocketing 20 gold, spotted by Lyra, Dru. Vote to forgive, demand a cut, banish or attack.");
    }

    [Fact]
    public void TraitorSentences()
    {
        ChaosText.TraitorBriefing.Should().Be(
            "You are the Traitor. Win by seeing the party wiped before floor 5, or by extracting alone while carrying the Cursed Idol. " +
            "Smile. Share. Betray at the right moment.");
        ChaosText.TraitorReveal("Merlin", TraitorOutcome.TraitorWinsWipe).Should().Be("Merlin was the Traitor, and wins: the party fell before floor 5.");
        ChaosText.TraitorReveal("Merlin", TraitorOutcome.TraitorWinsSoloExtraction).Should().Be(
            "Merlin was the Traitor, and wins: they extracted alone with the Cursed Idol.");
    }
}
