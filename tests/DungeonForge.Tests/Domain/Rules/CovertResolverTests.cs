using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using FluentAssertions;
using DungeonForge.Tests.Features.Parties.Chaos;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Domain.Rules;

public class CovertResolverTests
{
    private static PlayerAction Act(string name, CovertKind? kind = null, string target = "") =>
        new(Guid.NewGuid(), name, "press on", kind is { } k ? new CovertIntent(k, target) : null);

    private static IReadOnlyList<PartyMember> Members => PartyTestBuilders.Roster();

    [Fact]
    public void NoCovertIntents_ConsumesNoRollsAndResolvesNothing()
    {
        var roller = new ScriptedRoller(5);

        var result = CovertResolver.Resolve([Act("Conan"), Act("Merlin")], Members, roller);

        result.Outcomes.Should().BeEmpty();
        result.Hits.Should().BeEmpty();
        result.Sabotaged.Should().BeEmpty();
        roller.Next(100).Should().Be(5);
    }

    [Fact]
    public void Backstab_SucceedsBelowChance_DealsFixedDamage()
    {
        var result = CovertResolver.Resolve([Act("Conan", CovertKind.Backstab, "Merlin")], Members, new ScriptedRoller(10));

        var outcome = result.Outcomes.Single();
        outcome.Success.Should().BeTrue();
        outcome.Amount.Should().Be(CovertResolver.BackstabDamage);
        result.Hits.Should().Equal(new DamageResult("Merlin", CovertResolver.BackstabDamage));
        outcome.Log.Should().Contain("Conan").And.Contain("Merlin").And.Contain("backstab");
    }

    [Fact]
    public void Backstab_FailsAtOrAboveChance_AndDealsNothing()
    {
        var result = CovertResolver.Resolve([Act("Conan", CovertKind.Backstab, "Merlin")], Members,
            new ScriptedRoller(CovertResolver.BasePercent));

        result.Outcomes.Single().Success.Should().BeFalse();
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public void BardGetsABonusAtBackstabbing()
    {
        CovertResolver.ChancePercent(CovertKind.Backstab, CharacterClass.Bard)
            .Should().BeGreaterThan(CovertResolver.ChancePercent(CovertKind.Backstab, CharacterClass.Warrior));
        CovertResolver.ChancePercent(CovertKind.Sabotage, CharacterClass.Mage)
            .Should().BeGreaterThan(CovertResolver.ChancePercent(CovertKind.Sabotage, CharacterClass.Warrior));
    }

    [Fact]
    public void Sabotage_MarksTargetsOvertActionAsSabotaged()
    {
        var result = CovertResolver.Resolve([Act("Merlin", CovertKind.Sabotage, "Dru")], Members, new ScriptedRoller(1));

        result.Sabotaged.Should().BeEquivalentTo("Dru");
        result.Outcomes.Single().Log.Should().Contain("sabotage");
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public void Steal_DrainsTargetAndHealsThief()
    {
        var result = CovertResolver.Resolve([Act("Lyra", CovertKind.Steal, "Conan")], Members, new ScriptedRoller(1));

        result.Hits.Should().Equal(new DamageResult("Conan", CovertResolver.StealAmount));
        result.Heals.Should().Equal(("Lyra", CovertResolver.StealAmount));
        result.Outcomes.Single().Log.Should().Contain("stole");
    }

    [Theory]
    [InlineData("Conan")]
    [InlineData("conan")]
    [InlineData("Nobody")]
    [InlineData("")]
    public void InvalidTargets_AreIgnoredSilently(string target)
    {
        var result = CovertResolver.Resolve([Act("Conan", CovertKind.Backstab, target)], Members, new ScriptedRoller(1));

        result.Outcomes.Should().BeEmpty();
    }

    [Fact]
    public void DefeatedActorsOrTargets_AreIgnored()
    {
        var party = PartyTestBuilders.NewParty();
        party.ApplyMemberOutcomes([new DamageResult("Merlin", 99)], 0);

        var deadTarget = CovertResolver.Resolve([Act("Conan", CovertKind.Backstab, "Merlin")], party.Members, new ScriptedRoller(1));
        var deadActor = CovertResolver.Resolve([Act("Merlin", CovertKind.Backstab, "Conan")], party.Members, new ScriptedRoller(1));

        deadTarget.Outcomes.Should().BeEmpty();
        deadActor.Outcomes.Should().BeEmpty();
    }

    [Fact]
    public void ResolutionIsDeterministicInActionOrder()
    {
        PlayerAction[] actions =
        [
            Act("Conan", CovertKind.Backstab, "Dru"),
            Act("Merlin", CovertKind.Sabotage, "Conan"),
            Act("Lyra", CovertKind.Steal, "Merlin"),
        ];

        var a = CovertResolver.Resolve(actions, Members, new ScriptedRoller(10, 80, 10));
        var b = CovertResolver.Resolve(actions, Members, new ScriptedRoller(10, 80, 10));

        a.Outcomes.Select(o => (o.Actor, o.Kind, o.Success)).Should().Equal(
            ("Conan", CovertKind.Backstab, true), ("Merlin", CovertKind.Sabotage, false), ("Lyra", CovertKind.Steal, true));
        b.Outcomes.Should().Equal(a.Outcomes);
    }
}
