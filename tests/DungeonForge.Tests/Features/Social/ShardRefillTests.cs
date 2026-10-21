using DungeonForge.Domain;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Support;
using FluentAssertions;

namespace DungeonForge.Tests.Features.Social;

public class ShardRefillTests
{
    private static void Descend(Party party) => party.AdvanceProgress(RoomType.Boss, 999);

    [Fact]
    public void NothingIsGivenOnTheFirstFloor()
    {
        var party = PartyTestBuilders.NewParty();

        ShardRefill.Apply(party);

        party.Social().Profile("Conan").AskTokens.Should().Be(PlayerProfile.StartingTokens);
        party.Social().Outbox.Should().BeEmpty();
    }

    [Fact]
    public void EachNewFloor_GivesEveryMemberOneShard_AndTheirOwnPrivateNotice()
    {
        var party = PartyTestBuilders.NewParty();
        Descend(party);

        ShardRefill.Apply(party);

        foreach (var m in party.Members)
        {
            party.Social().Profile(m.Name).AskTokens.Should().Be(PlayerProfile.StartingTokens + 1);
        }

        party.Social().Outbox.Select(n => n.To).Should().BeEquivalentTo(party.Members.Select(m => m.Name));
        party.Social().Outbox.Should().OnlyContain(n => n.To != null && n.Text.Contains("whisper-shard"));
    }

    [Fact]
    public void ApplyingTwiceOnTheSameFloor_PaysOnce()
    {
        var party = PartyTestBuilders.NewParty();
        Descend(party);

        ShardRefill.Apply(party);
        ShardRefill.Apply(party);

        party.Social().Profile("Conan").AskTokens.Should().Be(PlayerProfile.StartingTokens + 1);
    }

    [Fact]
    public void SkippedFloors_AreAllPaid_ButTheStashIsCapped()
    {
        var party = PartyTestBuilders.NewParty();
        for (var i = 0; i < 6; i++) Descend(party);

        ShardRefill.Apply(party);

        party.Social().Profile("Conan").AskTokens.Should().Be(ShardRefill.MaxTokens);
    }

    [Fact]
    public void DefeatedMembers_AreNotPaid()
    {
        var party = PartyTestBuilders.NewParty();
        party.ApplyMemberOutcomes([new DamageResult("Merlin", 99)], 0);
        Descend(party);

        ShardRefill.Apply(party);

        party.Social().Profile("Merlin").AskTokens.Should().Be(PlayerProfile.StartingTokens);
        party.Social().Profile("Conan").AskTokens.Should().Be(PlayerProfile.StartingTokens + 1);
    }
}
