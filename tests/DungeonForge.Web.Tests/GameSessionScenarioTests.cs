using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;
using static DungeonForge.Web.Tests.SessionData;

namespace DungeonForge.Web.Tests;

public class GameSessionScenarioTests
{
    [Fact]
    public void ApplyDiscussion_ReplacesList_ClearedWhenTurnResolves()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var p = new PlayerAction(Guid.NewGuid(), "A", "hit it");

        sut.ApplyDiscussion(Id, [p]).Should().BeTrue();
        sut.Discussion.Should().Equal(p);
        sut.ApplyDiscussion(Guid.NewGuid(), []).Should().BeFalse();

        sut.Apply(Turn(1, RoomType.Chest, null, 0, 1, 1));
        sut.Discussion.Should().BeEmpty();
    }

    [Fact]
    public void ApplyPrompt_WithDiscussion_StoresIt()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var p = new PlayerAction(Guid.NewGuid(), "A", "hit it");

        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Puzzle, null, 0, 0, "r", 0, false, Discussion: [p], Scenario: "r"));

        sut.Discussion.Should().Equal(p);
    }

    [Fact]
    public void ApplyPrompt_SameTurn_BypassesTurnGuard_AndSetsScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        sut.ApplyNarration(Id, 2, "done");
        var changed = 0;
        sut.Changed += () => changed++;

        sut.ApplyPrompt(Prompt(2)).Should().BeTrue();

        sut.HasScenario.Should().BeTrue();
        sut.Scenario.Should().Be("A troll\nblocks the bridge.");
        sut.ScenarioPending.Should().BeFalse();
        sut.Turn.Should().Be(2);
        sut.Log.Should().HaveCount(1);
        changed.Should().Be(1);
    }

    [Fact]
    public void ApplyPrompt_FromFreshSession_Works()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0)).Should().BeTrue();
        sut.HasScenario.Should().BeTrue();
    }

    [Fact]
    public void ApplyPrompt_ScenarioPending_SetsPendingWithoutText()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyPrompt(new ExploreResponse(Id, 1, RoomType.Combat, null, 0, 0, "x", 0, false, ScenarioPending: true)).Should().BeTrue();

        sut.ScenarioPending.Should().BeTrue();
        sut.HasScenario.Should().BeFalse();
    }

    [Fact]
    public void ApplyPrompt_StaleTurnOtherPartyOrNoScenario_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 5));
        sut.ApplyNarration(Id, 2, "done");

        sut.ApplyPrompt(Prompt(1)).Should().BeFalse();
        sut.ApplyPrompt(Prompt(2) with { PartyId = Guid.NewGuid() }).Should().BeFalse();
        sut.ApplyPrompt(Prompt(2) with { Scenario = null }).Should().BeFalse();
        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void Apply_ClearsScenario_AwaitsNext_AndRecordsLastOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(Prompt(0));

        sut.Apply(Turn(1, RoomType.Combat, "A", 3, 0, 0) with { LastOutcome = "You won." }).Should().BeTrue();

        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeTrue();
        sut.LastOutcome.Should().Be("You won.");
        sut.Log[0].Outcome.Should().Be("You won.");
    }

    [Fact]
    public void Apply_PartyDefeated_DoesNotAwaitScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Combat, "A", 99, 0, 0, defeated: true));
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void BuildActions_OwnTextPlusTeammates_ReplacesOwnSubmitted()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var mate = new PlayerAction(Guid.NewGuid(), "B", "Read glyphs");
        sut.ApplyDiscussion(Id, [new PlayerAction(sut.PlayerId, "A", "old idea"), mate]);

        var actions = sut.BuildActions("  New idea ");

        actions.Should().HaveCount(2);
        actions.Should().Contain(mate);
        actions.Should().Contain(new PlayerAction(sut.PlayerId, sut.PlayerName, "New idea"));
    }

    [Fact]
    public void BuildActions_BlankOwnText_KeepsOwnSubmittedAction()
    {
        var sut = new GameSession();
        sut.Start(Party());
        var own = new PlayerAction(sut.PlayerId, "A", "old idea");
        sut.ApplyDiscussion(Id, [own]);

        sut.BuildActions("   ").Should().Equal(own);
    }

    [Fact]
    public void BuildActions_NothingAnywhere_IsEmpty_AndIgnoresBlankTeammateText()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyDiscussion(Id, [new PlayerAction(Guid.NewGuid(), "B", "  ")]);

        sut.BuildActions(null).Should().BeEmpty();
    }
}
