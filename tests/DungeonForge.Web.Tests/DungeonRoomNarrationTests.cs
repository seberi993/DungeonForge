using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class DungeonRoomNarrationTests : DungeonRoomTestBase
{
    [Fact]
    public void Resolve_WithOutcome_AppliesMechanicsAndShowsOutcome()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ResolveWithOutcomeJson()));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find(".log-entry").TextContent.Should().Contain("You bribed the troll and slipped past.");
        cut.Find("#last-outcome").TextContent.Should().Contain("You bribed the troll");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#member-0").TextContent.Should().Contain("27/30");
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NarrationPending_AppliesMechanicsImmediately_WithPlaceholders_AndNoExplore()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#stat-gold").TextContent.Should().Contain("20"));
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#narration-loading").Should().NotBeNull();
        cut.Find("#scenario-pending").TextContent.Should().Contain("dungeon shifts");
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
        cut.FindAll("#explore").Should().BeEmpty();
    }

    [Fact]
    public void NarrationReady_MatchingTurn_ShowsOutcomeAndNextScenario()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "The runes hum and the door opens.", "A vast hall\nstretches ahead.", RoomType.Chest)));

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().Contain("door opens"));
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.Find(".log-outcome").TextContent.Should().Contain("door opens");
        cut.Find("#scenario").TextContent.Should().Be("A vast hall\nstretches ahead.");
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void NarrationReady_WrongTurn_IsIgnored()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 5, "wrong", "wrong scenario")));

        cut.Find("#narration-loading").Should().NotBeNull();
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void NarrationReady_PartyDefeated_NoNextScenario_ShowsOutcomeOnly()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "Darkness takes you.")));

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().Contain("Darkness"));
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void NarrationNeverArrives_FallsBackToPlaceholderText_AndOffersExploreAgain()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()), TimeSpan.FromMilliseconds(50));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().NotBeNullOrWhiteSpace(), TimeSpan.FromSeconds(3));
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Resolve_Twice_AppendsEntries_ViaNextScenario()
    {
        var turn = 0;
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, bodyFactory: () => ExploreJson(++turn, "combat", "Conan", 1, 0, "Step", 0)));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "o", "Next room.")));
        cut.WaitForAssertion(() => cut.Find("#scenario").TextContent.Should().Be("Next room."));

        Resolve(cut, "Again");

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(2));
    }

    [Fact]
    public void TurnWithBeats_ActsItOutOnTheStage_AndHoldsTheTextUntilDone()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));
        ExploreResponse resolved = new(Id, 1, RoomType.Combat, "Conan", 3, 0, "Clash", 0, false, NarrationPending: true,
            Beats: [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "I charge the troll"), new ActionBeat("Merlin", CharacterClass.Mage, ActionVerb.Cast, "A fireball")],
            Success: true);

        cut.InvokeAsync(() => _hub.RaiseTurn(resolved));
        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "The troll falls.", "A **door** looms.", RoomType.Puzzle, "door")));

        cut.WaitForAssertion(() => cut.Find(".stage-caption").TextContent.Should().Contain("Conan").And.Contain("I charge the troll"));
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario-panel").Should().BeEmpty();
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#ascii-stage").Should().HaveCount(1);
    }

    [Fact]
    public void TurnWithoutBeats_KeepsTheClassicFlow()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Combat, "Conan", 3, 0, "Clash", 0) with { NarrationPending = true }));

        cut.WaitForAssertion(() => cut.Find("#narration-loading"));
    }
}
