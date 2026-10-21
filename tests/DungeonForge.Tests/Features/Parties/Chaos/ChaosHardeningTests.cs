using DungeonForge.Domain.Rules;
using DungeonForge.Domain;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties.Chaos;

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public class ConfrontationTimeoutTests
{
    private static (Party Party, ManualTime Clock) Caught()
    {
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        var party = Party.Create(
            Guid.NewGuid(), "Heroes", PartyTestBuilders.Roster(), clock.GetUtcNow(), "", PartyTestBuilders.ChaosOn, clock);
        party.DropVault(20, null);
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(30)); // Lyra and Dru spot; voters Merlin, Lyra, Dru
        return (party, clock);
    }

    [Fact]
    public void BeforeTheDeadline_NothingResolves()
    {
        var (party, clock) = Caught();
        clock.Advance(ConfrontationRules.Timeout - TimeSpan.FromSeconds(1));

        party.TryResolveConfrontation(new ScriptedRoller(0)).Should().BeNull();
        party.Confrontation.Should().NotBeNull();
    }

    [Fact]
    public void AfterTheDeadline_MissingVotersAbstain_AndCastBallotsDecide()
    {
        var (party, clock) = Caught();
        party.CastVote("Merlin", ConfrontationVote.Banish, new ScriptedRoller(0));
        clock.Advance(ConfrontationRules.Timeout + TimeSpan.FromSeconds(1));

        var resolution = party.TryResolveConfrontation(new ScriptedRoller(0));

        resolution!.Outcome.Should().Be(ConfrontationVote.Banish);
        party.Confrontation.Should().BeNull();
        party.IsBanished("Conan").Should().BeTrue();
    }

    [Fact]
    public void AfterTheDeadline_WithNoBallots_ForgivesAndUnblocksTheVault()
    {
        var (party, clock) = Caught();
        clock.Advance(ConfrontationRules.Timeout + TimeSpan.FromSeconds(1));

        party.ClaimVault("Merlin", VaultChoice.Share, new ScriptedRoller(0)).Status.Should().Be(VaultClaimStatus.NoVault);
        party.Confrontation.Should().BeNull();
        party.IsBanished("Conan").Should().BeFalse();
        party.PocketedGold("Conan").Should().Be(20);
    }

    [Fact]
    public void LateVote_ResolvesTheExpiredConfrontationInsteadOfHangingIt()
    {
        var (party, clock) = Caught();
        clock.Advance(ConfrontationRules.Timeout + TimeSpan.FromSeconds(1));

        var result = party.CastVote("Merlin", ConfrontationVote.Attack, new ScriptedRoller(0));

        result.Status.Should().Be(VoteStatus.Resolved);
        party.Confrontation.Should().BeNull();
    }

    [Fact]
    public void WhenEveryRemainingVoterIsOut_ItResolvesWithoutATimeout()
    {
        var (party, _) = Caught();
        party.ApplyMemberOutcomes([new("Merlin", 999), new("Lyra", 999), new("Dru", 999)], 0);

        var resolution = party.TryResolveConfrontation(new ScriptedRoller(0));

        resolution!.Outcome.Should().Be(ConfrontationVote.Forgive);
        party.Confrontation.Should().BeNull();
    }

    [Fact]
    public void WhenRemainingEligibleVotersAllVoted_ItResolves()
    {
        var (party, _) = Caught();
        party.CastVote("Merlin", ConfrontationVote.Attack, new ScriptedRoller(0));
        party.ApplyMemberOutcomes([new("Lyra", 999), new("Dru", 999)], 0);

        party.TryResolveConfrontation(new ScriptedRoller(0))!.Outcome.Should().Be(ConfrontationVote.Attack);
    }
}

public class ExtractionHardeningTests
{
    private static Party Pact()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));
        party.TryAssignTraitor(new ScriptedRoller(0)); // Conan
        return party;
    }

    [Fact]
    public void Extraction_IsOneShot()
    {
        var party = Pact();
        party.DropVault(10, "Cursed Idol");
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(99));

        party.TryExtract("Conan").Should().BeTrue();
        party.TryExtract("Merlin").Should().BeFalse();
        party.ApplyMemberOutcomes([new("Merlin", 99), new("Lyra", 99), new("Dru", 99)], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.TraitorWinsSoloExtraction);
    }

    [Fact]
    public void Extraction_RemovesTheExtractorFromPlay()
    {
        var party = Pact();

        party.TryExtract("Merlin").Should().BeTrue();

        party.Snapshot().Members.Single(m => m.Name == "Merlin").IsDefeated.Should().BeTrue();
        party.RequiredLocks.Should().Be(3);
        party.TryExtract("Merlin").Should().BeFalse();
    }

    [Fact]
    public void Extraction_IsRefusedForBanishedMembers()
    {
        var party = Pact();
        party.DropVault(5, null);
        party.ClaimVault("Merlin", VaultChoice.Pocket, new ScriptedRoller(30));
        party.CastVote("Conan", ConfrontationVote.Banish, new ScriptedRoller(0));
        party.CastVote("Lyra", ConfrontationVote.Banish, new ScriptedRoller(0));
        party.CastVote("Dru", ConfrontationVote.Banish, new ScriptedRoller(0));

        party.IsBanished("Merlin").Should().BeTrue();
        party.TryExtract("Merlin").Should().BeFalse();
    }

    [Fact]
    public void TraitorWin_IsAnnouncedOnce()
    {
        var party = Pact();
        party.ApplyMemberOutcomes([new("Merlin", 999), new("Lyra", 999), new("Dru", 999)], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.TraitorWinsWipe);
        party.EvaluateTraitor().Should().Be(TraitorOutcome.None);
        party.TraitorResult.Should().Be(TraitorOutcome.TraitorWinsWipe);
    }
}

public class VaultHardeningTests
{
    private static Party Chaos() => PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);

    [Fact]
    public void DropVault_KeepsEveryArtifact_AndAccumulatesGold()
    {
        var party = Chaos();

        party.DropVault(10, "Cursed Idol");
        party.DropVault(5, "Moon Blade");

        party.Vault!.Gold.Should().Be(15);
        party.Vault.Artifacts.Should().Equal("Cursed Idol", "Moon Blade");
    }

    [Fact]
    public void ClaimVault_AwardsEveryArtifact()
    {
        var party = Chaos();
        party.DropVault(10, "Cursed Idol");
        party.DropVault(5, "Moon Blade");

        party.ClaimVault("Lyra", VaultChoice.Share, new ScriptedRoller(0));

        party.ArtifactHolder("Cursed Idol").Should().Be("Lyra");
        party.ArtifactHolder("Moon Blade").Should().Be("Lyra");
    }

    [Fact]
    public void WipeWithAnUnclaimedVault_BanksTheGold()
    {
        // Policy: unclaimed vault gold is banked into the purse when the party wipes so the score counts it; artifacts are lost.
        var party = Chaos();
        party.DropVault(30, "Cursed Idol");

        party.ApplyMemberOutcomes([.. party.Members.Select(m => new DamageResult(m.Name, 999))], 0);

        party.Vault.Should().BeNull();
        party.Gold.Should().Be(30);
    }

    [Fact]
    public void DropOnTheWipingTurn_IsBankedImmediately()
    {
        var party = Chaos();
        party.ApplyMemberOutcomes([.. party.Members.Select(m => new DamageResult(m.Name, 999))], 0);

        party.DropVault(12, null);

        party.Vault.Should().BeNull();
        party.Gold.Should().Be(12);
    }
}

public class TraitorWinRuleTests
{
    [Fact]
    public void WipeWithADeadOrBanishedTraitor_IsNotATraitorWin() =>
        TraitorPact.Evaluate(new TraitorState(1, true, false, 0, false, false)).Should().Be(TraitorOutcome.None);

    [Fact]
    public void WipeOfEveryoneButTheTraitor_IsATraitorWin()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));
        party.TryAssignTraitor(new ScriptedRoller(0)); // Conan
        party.ApplyMemberOutcomes([new("Merlin", 999), new("Lyra", 999), new("Dru", 999)], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.TraitorWinsWipe);
    }

    [Fact]
    public void FullWipeIncludingTheTraitor_IsNotATraitorWin()
    {
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));
        party.TryAssignTraitor(new ScriptedRoller(0));
        party.ApplyMemberOutcomes([.. party.Members.Select(m => new DamageResult(m.Name, 999))], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.None);
    }
}
