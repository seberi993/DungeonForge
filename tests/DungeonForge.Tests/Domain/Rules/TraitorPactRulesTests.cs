using DungeonForge.Domain;
using DungeonForge.Domain.Rules;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class TraitorPactRulesTests
{
    private static TraitorState State(
        int floor = 1, bool wiped = false, bool traitorAlive = true, int othersAlive = 2, bool idol = false, bool extracted = false) =>
        new(floor, wiped, traitorAlive, othersAlive, idol, extracted);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void PartyWipeBeforeFloorFive_IsATraitorWin(int floor) =>
        TraitorPact.Evaluate(State(floor, wiped: true, traitorAlive: true, othersAlive: 0))
            .Should().Be(TraitorOutcome.TraitorWinsWipe);

    [Fact]
    public void PartyWipeOnFloorFiveOrLater_IsNotATraitorWin() =>
        TraitorPact.Evaluate(State(5, wiped: true, traitorAlive: false, othersAlive: 0)).Should().Be(TraitorOutcome.None);

    [Fact]
    public void SoloExtractionWithTheIdol_IsATraitorWin() =>
        TraitorPact.Evaluate(State(7, othersAlive: 0, idol: true, extracted: true))
            .Should().Be(TraitorOutcome.TraitorWinsSoloExtraction);

    [Theory]
    [InlineData(false, true, 0, true)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, true, 1, true)]
    [InlineData(true, true, 0, false)]
    public void SoloExtraction_NeedsIdolExtractionAndNoSurvivors(bool idol, bool extracted, int othersAlive, bool alive) =>
        TraitorPact.Evaluate(State(7, traitorAlive: alive, othersAlive: othersAlive, idol: idol, extracted: extracted))
            .Should().Be(alive && idol && extracted && othersAlive == 0 ? TraitorOutcome.TraitorWinsSoloExtraction : TraitorOutcome.None);

    [Fact]
    public void OrdinaryProgress_IsNoOutcome() =>
        TraitorPact.Evaluate(State()).Should().Be(TraitorOutcome.None);
}
