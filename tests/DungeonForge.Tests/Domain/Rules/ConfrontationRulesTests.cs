using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class ConfrontationRulesTests
{
    [Fact]
    public void Decide_StrictMajorityWins()
    {
        ConfrontationRules.Decide(
            [ConfrontationVote.Banish, ConfrontationVote.Banish, ConfrontationVote.Forgive], new ScriptedRoller(0))
            .Should().Be(ConfrontationVote.Banish);
    }

    [Fact]
    public void Decide_PluralityWinsWithoutAMajority()
    {
        ConfrontationRules.Decide(
            [ConfrontationVote.Attack, ConfrontationVote.Attack, ConfrontationVote.Forgive, ConfrontationVote.DemandCut], new ScriptedRoller(0))
            .Should().Be(ConfrontationVote.Attack);
    }

    [Theory]
    [InlineData(0, ConfrontationVote.Forgive)]
    [InlineData(1, ConfrontationVote.Banish)]
    public void Decide_TiesAreBrokenByTheRollerAmongTiedOptionsInEnumOrder(int roll, ConfrontationVote expected)
    {
        ConfrontationRules.Decide(
            [ConfrontationVote.Banish, ConfrontationVote.Forgive], new ScriptedRoller(roll)).Should().Be(expected);
    }

    [Fact]
    public void Decide_ATieNeverConsultsTheRollerWhenThereIsAWinner()
    {
        var roller = new ScriptedRoller(1, 2);

        ConfrontationRules.Decide([ConfrontationVote.Attack, ConfrontationVote.Attack, ConfrontationVote.Forgive], roller);

        roller.Next(100).Should().Be(1);
    }

    [Fact]
    public void Tally_CountsEveryOption()
    {
        var tally = ConfrontationRules.Tally([ConfrontationVote.Attack, ConfrontationVote.Attack, ConfrontationVote.Forgive]);

        tally[ConfrontationVote.Attack].Should().Be(2);
        tally[ConfrontationVote.Forgive].Should().Be(1);
        tally[ConfrontationVote.Banish].Should().Be(0);
    }

    [Fact]
    public void Outcome_Forgive_LetsThemKeepEverything() =>
        ConfrontationRules.Outcome(ConfrontationVote.Forgive, 21)
            .Should().Be(new ConfrontationEffect(0, 21, false, 0, true));

    [Fact]
    public void Outcome_DemandCut_SplitsHalfRoundedUpToTheParty() =>
        ConfrontationRules.Outcome(ConfrontationVote.DemandCut, 21)
            .Should().Be(new ConfrontationEffect(11, 10, false, 0, true));

    [Fact]
    public void Outcome_Banish_ForfeitsEverythingAndRemovesThem() =>
        ConfrontationRules.Outcome(ConfrontationVote.Banish, 21)
            .Should().Be(new ConfrontationEffect(21, 0, true, 0, false));

    [Fact]
    public void Outcome_Attack_HurtsAndForfeits() =>
        ConfrontationRules.Outcome(ConfrontationVote.Attack, 21)
            .Should().Be(new ConfrontationEffect(21, 0, false, ConfrontationRules.AttackDamage, false));
}
