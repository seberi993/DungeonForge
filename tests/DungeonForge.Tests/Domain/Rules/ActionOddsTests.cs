using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class ActionOddsTests
{
    [Theory]
    [InlineData("I attack the goblin", ActionKind.Attack)]
    [InlineData("run away!", ActionKind.Evade)]
    [InlineData("grab the chest", ActionKind.Loot)]
    [InlineData("hum a tune", ActionKind.Other)]
    public void Classify_PicksTheKind(string text, ActionKind expected) =>
        ActionOdds.Classify(text).Should().Be(expected);

    [Fact]
    public void Percent_SoloAndUnrelatedPlansAreAFlipOfACoin() =>
        ActionOdds.Percent(ActionKind.Attack, []).Should().Be(50);

    [Fact]
    public void Percent_TeamworkHelps_ClashHurts_BothCapped()
    {
        ActionOdds.Percent(ActionKind.Attack, [ActionKind.Attack]).Should().Be(60);
        ActionOdds.Percent(ActionKind.Attack, [ActionKind.Attack, ActionKind.Attack, ActionKind.Attack, ActionKind.Attack]).Should().Be(70);
        ActionOdds.Percent(ActionKind.Attack, [ActionKind.Evade]).Should().Be(40);
        ActionOdds.Percent(ActionKind.Attack, [ActionKind.Evade, ActionKind.Loot, ActionKind.Evade, ActionKind.Evade]).Should().Be(30);
        ActionOdds.Percent(ActionKind.Other, [ActionKind.Attack, ActionKind.Evade]).Should().Be(50);
    }

    [Fact]
    public void For_ComputesEachActionAgainstItsTeammates()
    {
        PlayerAction Act(string text) => new(Guid.NewGuid(), "X", text);

        ActionOdds.For([Act("attack"), Act("smash it"), Act("run")]).Should().Equal(50, 50, 30);
    }
}
