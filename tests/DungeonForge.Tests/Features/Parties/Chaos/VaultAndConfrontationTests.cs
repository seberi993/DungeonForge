using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class PartyVaultTests
{
    private static Party Chaos() => PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn);

    [Fact]
    public void DropVault_CreatesAnUnclaimedVault_AndMergesLaterDrops()
    {
        var party = Chaos();

        party.DropVault(10, null);
        party.DropVault(5, "Cursed Idol");

        party.Vault!.Gold.Should().Be(15);
        party.Vault.Artifact.Should().Be("Cursed Idol");
        party.Gold.Should().Be(0);
    }

    [Fact]
    public void DropVault_IgnoresEmptyDrops()
    {
        var party = Chaos();

        party.DropVault(0, null);

        party.Vault.Should().BeNull();
    }

    [Fact]
    public void ClaimVault_Share_PaysThePartyAndClearsTheVault()
    {
        var party = Chaos();
        party.DropVault(20, "Cursed Idol");

        var claim = party.ClaimVault("Conan", VaultChoice.Share, new ScriptedRoller(0));

        claim.Status.Should().Be(VaultClaimStatus.Shared);
        party.Gold.Should().Be(20);
        party.Vault.Should().BeNull();
        party.PocketedGold("Conan").Should().Be(0);
        party.ArtifactHolder("Cursed Idol").Should().Be("Conan");
        party.Confrontation.Should().BeNull();
    }

    [Fact]
    public void ClaimVault_PocketUnseen_KeepsItPrivate()
    {
        var party = Chaos();
        party.DropVault(20, null);

        // Everyone rolls 99: nobody beats even the 40% perception.
        var claim = party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(99));

        claim.Status.Should().Be(VaultClaimStatus.PocketedHidden);
        party.Gold.Should().Be(0);
        party.PocketedGold("Conan").Should().Be(20);
        party.Vault.Should().BeNull();
        party.Confrontation.Should().BeNull();
    }

    [Fact]
    public void ClaimVault_PocketCaught_OpensAPublicConfrontationWithTheSpotters()
    {
        var party = Chaos();
        party.DropVault(20, null);

        // Roll 30 with Conan as accused: Merlin (15%) misses, Lyra (Bard, 40%) and Dru (Druid, 40%) catch.
        var claim = party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(30));

        claim.Status.Should().Be(VaultClaimStatus.PocketedCaught);
        claim.Spotters.Should().Equal("Lyra", "Dru");
        var c = party.Confrontation!;
        c.Accused.Should().Be("Conan");
        c.Gold.Should().Be(20);
        c.Voters.Should().BeEquivalentTo("Merlin", "Lyra", "Dru");
        party.PocketedGold("Conan").Should().Be(20);
    }

    [Fact]
    public void ClaimVault_RejectsMissingVaultUnknownPlayerAndActiveConfrontation()
    {
        var party = Chaos();
        party.ClaimVault("Conan", VaultChoice.Share, new ScriptedRoller(0)).Status.Should().Be(VaultClaimStatus.NoVault);

        party.DropVault(5, null);
        party.ClaimVault("Ghost", VaultChoice.Share, new ScriptedRoller(0)).Status.Should().Be(VaultClaimStatus.UnknownPlayer);

        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(0));
        party.DropVault(5, null);
        party.ClaimVault("Merlin", VaultChoice.Share, new ScriptedRoller(0)).Status.Should().Be(VaultClaimStatus.ConfrontationActive);
    }

    [Fact]
    public void ClaimVault_AloneWithNoOneToWatch_CannotBeCaught()
    {
        var party = PartyTestBuilders.NewParty(PartyTestBuilders.ChaosOn, new PartyMember("Solo", CharacterClass.Bard));
        party.DropVault(5, null);

        party.ClaimVault("Solo", VaultChoice.Pocket, new ScriptedRoller(0)).Status.Should().Be(VaultClaimStatus.PocketedHidden);
    }

    private static Party CaughtParty()
    {
        var party = Chaos();
        party.DropVault(21, "Cursed Idol");
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(30));
        return party;
    }

    [Fact]
    public void CastVote_RecordsUntilEveryEligibleVoterHasVoted_ThenResolves()
    {
        var party = CaughtParty();

        party.CastVote("Merlin", ConfrontationVote.DemandCut, new ScriptedRoller(0)).Status.Should().Be(VoteStatus.Recorded);
        party.CastVote("Lyra", ConfrontationVote.DemandCut, new ScriptedRoller(0)).Status.Should().Be(VoteStatus.Recorded);
        var last = party.CastVote("Dru", ConfrontationVote.Forgive, new ScriptedRoller(0));

        last.Status.Should().Be(VoteStatus.Resolved);
        last.Resolution!.Outcome.Should().Be(ConfrontationVote.DemandCut);
        last.Resolution.GoldToParty.Should().Be(11);
        last.Resolution.GoldKept.Should().Be(10);
        party.Gold.Should().Be(11);
        party.PocketedGold("Conan").Should().Be(10);
        party.Confrontation.Should().BeNull();
        party.Vault.Should().BeNull();
    }

    [Fact]
    public void CastVote_RevoteReplacesTheEarlierVote()
    {
        var party = CaughtParty();
        party.CastVote("Merlin", ConfrontationVote.Attack, new ScriptedRoller(0));
        party.CastVote("Merlin", ConfrontationVote.Forgive, new ScriptedRoller(0));
        party.CastVote("Lyra", ConfrontationVote.Forgive, new ScriptedRoller(0));

        var last = party.CastVote("Dru", ConfrontationVote.Forgive, new ScriptedRoller(0));

        last.Resolution!.Outcome.Should().Be(ConfrontationVote.Forgive);
        last.Resolution.Tally[ConfrontationVote.Attack].Should().Be(0);
    }

    [Fact]
    public void CastVote_AccusedAndStrangersCannotVote_AndNoConfrontationMeansNoVote()
    {
        var party = CaughtParty();

        party.CastVote("Conan", ConfrontationVote.Forgive, new ScriptedRoller(0)).Status.Should().Be(VoteStatus.NotEligible);
        party.CastVote("Ghost", ConfrontationVote.Forgive, new ScriptedRoller(0)).Status.Should().Be(VoteStatus.NotEligible);
        Chaos().CastVote("Merlin", ConfrontationVote.Forgive, new ScriptedRoller(0)).Status.Should().Be(VoteStatus.NoConfrontation);
    }

    [Fact]
    public void Resolution_Banish_RemovesThePlayerAndHandsEverythingToTheParty()
    {
        var party = CaughtParty();
        party.CastVote("Merlin", ConfrontationVote.Banish, new ScriptedRoller(0));
        party.CastVote("Lyra", ConfrontationVote.Banish, new ScriptedRoller(0));

        var last = party.CastVote("Dru", ConfrontationVote.Forgive, new ScriptedRoller(0));

        last.Resolution!.Banished.Should().BeTrue();
        party.IsBanished("Conan").Should().BeTrue();
        party.Gold.Should().Be(21);
        party.PocketedGold("Conan").Should().Be(0);
        party.ArtifactHolder("Cursed Idol").Should().BeNull();
        party.Snapshot().Members.Single(m => m.Name == "Conan").IsDefeated.Should().BeTrue();
        party.RequiredLocks.Should().Be(3);
    }

    [Fact]
    public void Resolution_Attack_HurtsTheAccused()
    {
        var party = CaughtParty();
        var hpBefore = party.Members.Single(m => m.Name == "Conan").Hp;
        party.CastVote("Merlin", ConfrontationVote.Attack, new ScriptedRoller(0));
        party.CastVote("Lyra", ConfrontationVote.Attack, new ScriptedRoller(0));

        party.CastVote("Dru", ConfrontationVote.Forgive, new ScriptedRoller(0));

        party.Members.Single(m => m.Name == "Conan").Hp.Should().Be(hpBefore - ConfrontationRules.AttackDamage);
        party.Gold.Should().Be(21);
    }

    [Fact]
    public void Resolution_TiesUseTheInjectedRoller()
    {
        var a = CaughtParty();
        a.CastVote("Merlin", ConfrontationVote.Forgive, new ScriptedRoller(0));
        a.CastVote("Lyra", ConfrontationVote.Banish, new ScriptedRoller(0));
        a.CastVote("Dru", ConfrontationVote.Attack, new ScriptedRoller(0)).Resolution!.Outcome.Should().Be(ConfrontationVote.Forgive);

        var b = CaughtParty();
        b.CastVote("Merlin", ConfrontationVote.Forgive, new ScriptedRoller(2));
        b.CastVote("Lyra", ConfrontationVote.Banish, new ScriptedRoller(2));
        b.CastVote("Dru", ConfrontationVote.Attack, new ScriptedRoller(2)).Resolution!.Outcome.Should().Be(ConfrontationVote.Attack);
    }

    [Fact]
    public void BanishedMembers_NeverBlockTheTurnOrCountAsAlive()
    {
        var party = CaughtParty();
        party.CastVote("Merlin", ConfrontationVote.Banish, new ScriptedRoller(0));
        party.CastVote("Lyra", ConfrontationVote.Banish, new ScriptedRoller(0));
        party.CastVote("Dru", ConfrontationVote.Banish, new ScriptedRoller(0));

        party.IsPartyDefeated.Should().BeFalse();
    }

    [Fact]
    public void PocketedLoot_IsNeverInThePublicSnapshot()
    {
        var party = Chaos();
        party.DropVault(20, "Cursed Idol");
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(99));

        var json = System.Text.Json.JsonSerializer.Serialize(party.Snapshot());

        json.Should().NotContain("Cursed Idol").And.NotContain("Pocket");
        party.Snapshot().Gold.Should().Be(0);
    }
}
