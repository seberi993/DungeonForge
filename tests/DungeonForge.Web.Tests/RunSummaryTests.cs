using Bunit;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class RunSummaryTests : TestContext
{
    private static readonly Guid Id = Guid.NewGuid();

    private static MemberDto Member(string name, CharacterClass cls, int hp) => new(name, cls, hp, hp, false);

    private static PartyResponse Party() =>
        new(Id, "Heroes", [Member("Conan", CharacterClass.Warrior, 30), Member("Merlin", CharacterClass.Mage, 16)], DateTimeOffset.UtcNow);

    private static RollDto Roll(string name, int roll, int chance = 50) =>
        new(name, roll, chance, roll < ActionOddsCritBelow, roll >= ActionOddsFumbleFrom);

    private const int ActionOddsCritBelow = 5;
    private const int ActionOddsFumbleFrom = 95;

    private static ExploreResponse Resolved(int turn, bool success, int found, int total, string? target, int damage, params RollDto[] rolls) =>
        new(Id, turn, RoomType.Combat, target, damage, found, "x", total, false, Success: success, Rolls: rolls,
            Hits: target is null ? null : [new HitDto(target, damage)]);

    private static GameSession TwoTurnRun()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Resolved(1, true, 12, 12, null, 0, Roll("Conan", 3), Roll("Merlin", 20)));
        sut.Apply(Resolved(2, false, 0, 12, "Merlin", 9, Roll("Conan", 30), Roll("Merlin", 97)));
        return sut;
    }

    [Fact]
    public void Summary_TalliesTheRunAcrossTurns()
    {
        var summary = TwoTurnRun().Summary;

        summary.Turns.Should().Be(2);
        summary.RoomsWon.Should().Be(1);
        summary.RoomsLost.Should().Be(1);
        summary.Crits.Should().Be(1);
        summary.Fumbles.Should().Be(1);
        summary.WorstHitName.Should().Be("Merlin");
        summary.WorstHit.Should().Be(9);
    }

    [Fact]
    public void Summary_MvpIsTheMemberWithTheMostPassedRolls_TiesGoToTheEarlierHero()
    {
        var sut = TwoTurnRun();

        // Conan passed twice (3<50, 30<50); Merlin once (20<50).
        sut.Summary.MvpName.Should().Be("Conan");
        sut.Summary.MvpPasses.Should().Be(2);
    }

    [Fact]
    public void Summary_WithoutAnyPassedRoll_HasNoMvp()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Resolved(1, false, 0, 0, "Conan", 6, Roll("Conan", 80)));

        sut.Summary.MvpName.Should().BeNull();
    }

    [Fact]
    public void Summary_VerdictIsTheNarratorsLastWord_AndClearResetsTheRun()
    {
        var sut = TwoTurnRun();
        sut.ApplyNarration(Id, 2, "Merlin, you absolute disaster.");

        sut.Summary.Verdict.Should().Be("Merlin, you absolute disaster.");
        sut.Summary.Gold.Should().Be(12);

        sut.Clear();
        sut.Summary.Should().Be(new RunSummary(0, 0, 0, 0, 0, 0, null, 0, null, 0, null));
    }

    [Fact]
    public void Component_ShowsTheNumbersTheMvpAndTheVerdict()
    {
        var sut = TwoTurnRun();
        sut.ApplyNarration(Id, 2, "Merlin, you absolute disaster.");

        var cut = RenderComponent<RunSummaryCard>(p => p.Add(c => c.Summary, sut.Summary));

        cut.Find(".run-summary").TextContent.Should()
            .Contain("2").And.Contain("12").And.Contain("Conan").And.Contain("Merlin").And.Contain("9")
            .And.Contain("Merlin, you absolute disaster.");
    }

    [Fact]
    public void Component_OmitsTheMvpAndVerdictLinesWhenThereIsNone()
    {
        var cut = RenderComponent<RunSummaryCard>(p => p.Add(c => c.Summary, new RunSummary(0, 0, 0, 0, 0, 0, null, 0, null, 0, null)));

        cut.FindAll(".run-mvp").Should().BeEmpty();
        cut.FindAll(".run-verdict").Should().BeEmpty();
    }
}
