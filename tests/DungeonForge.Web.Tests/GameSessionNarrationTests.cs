using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;
using static DungeonForge.Web.Tests.SessionData;

namespace DungeonForge.Web.Tests;

public class GameSessionNarrationTests
{
    [Fact]
    public void Apply_NarrationPending_AppliesMechanicsAndMarksPending()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.Apply(Pending(1)).Should().BeTrue();

        sut.Members[0].Hp.Should().Be(26);
        sut.Turn.Should().Be(1);
        sut.NarrationPending.Should().BeTrue();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_MatchingTurn_SetsOutcomeAndLogEntry()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "You win.").Should().BeTrue();

        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().Be("You win.");
        sut.Log[0].Outcome.Should().Be("You win.");
    }

    [Fact]
    public void ApplyNarration_EmptyOutcome_FirstTurn_SetsScenarioWithoutLoggingBlankOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, "", 0, false, ScenarioPending: true));

        sut.ApplyNarration(Id, 0, "", "A door looms.").Should().BeTrue();

        sut.Scenario.Should().Be("A door looms.");
        sut.ScenarioPending.Should().BeFalse();
        sut.LastOutcome.Should().BeNull();
        sut.Log.Should().BeEmpty();
    }

    [Fact]
    public void ApplyNarration_EmptyOutcome_DoesNotOverwriteExistingOutcome()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));
        sut.ApplyNarration(Id, 1, "You win.", "Next room.");

        sut.ApplyNarration(Id, 1, "", "Other.").Should().BeFalse();

        sut.LastOutcome.Should().Be("You win.");
        sut.Log[0].Outcome.Should().Be("You win.");
    }

    [Fact]
    public void ApplyNarration_OtherTurnOrParty_IsIgnored()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(2));

        sut.ApplyNarration(Id, 1, "old").Should().BeFalse();
        sut.ApplyNarration(Guid.NewGuid(), 2, "x").Should().BeFalse();

        sut.NarrationPending.Should().BeTrue();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_ArrivingBeforeResponse_IsAppliedWhenTurnLands()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyNarration(Id, 1, "early");
        sut.Apply(Pending(1));

        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().Be("early");
        sut.Log[0].Outcome.Should().Be("early");
    }

    [Fact]
    public void ExpireNarration_UsesPlaceholderOnlyIfStillPending_AndLateNarrationStillWins()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ExpireNarration(1).Should().BeTrue();
        sut.NarrationPending.Should().BeFalse();
        sut.LastOutcome.Should().NotBeNullOrWhiteSpace();

        sut.ApplyNarration(Id, 1, "late").Should().BeTrue();
        sut.LastOutcome.Should().Be("late");
        sut.ExpireNarration(1).Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_WithNextScenario_BecomesCurrentScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "You win.", "A new hall\nopens.");

        sut.Scenario.Should().Be("A new hall\nopens.");
        sut.ScenarioPending.Should().BeFalse();
        sut.NarrationPending.Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_WithoutNextScenario_ClearsScenarioPending()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ApplyNarration(Id, 1, "The end.");

        sut.HasScenario.Should().BeFalse();
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ApplyNarration_OldTurn_DoesNotOverrideCurrentScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));
        sut.Apply(Pending(2));

        sut.ApplyNarration(Id, 1, "old", "stale scenario");

        sut.Scenario.Should().BeNull();
        sut.ScenarioPending.Should().BeTrue();
    }

    [Fact]
    public void ApplyNarration_ArrivingBeforeResponse_KeepsNextScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());

        sut.ApplyNarration(Id, 1, "early", "Next room.");
        sut.Apply(Pending(1));

        sut.Scenario.Should().Be("Next room.");
        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ExpireNarration_AlsoStopsWaitingForScenario()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Pending(1));

        sut.ExpireNarration(1).Should().BeTrue();

        sut.ScenarioPending.Should().BeFalse();
    }

    [Fact]
    public void ExpireNarration_ScenarioPendingOnly_Expires()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 1, 1));
        sut.ScenarioPending.Should().BeTrue();

        sut.ExpireNarration(1).Should().BeTrue();

        sut.ScenarioPending.Should().BeFalse();
        sut.LastOutcome.Should().BeNull();
    }

    [Fact]
    public void ApplyNarration_RemembersWhereTheOutcomeCameFrom_UntilTheNextTurn()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.Apply(Turn(1, RoomType.Chest, null, 0, 5, 5));

        sut.ApplyNarration(Id, 1, "Canned.", "Next. What do you do?", RoomType.Combat, null, "fallback");

        sut.NarrationSource.Should().Be("fallback");
        sut.Apply(Turn(2, RoomType.Chest, null, 0, 5, 10));
        sut.NarrationSource.Should().BeNull();
    }
}
