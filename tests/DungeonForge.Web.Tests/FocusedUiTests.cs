using Bunit;
using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Dungeon.Stage;
using DungeonForge.Web.Features.Lobby;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class FocusedUiTests : TestContext
{
    private static readonly Guid Id = Guid.NewGuid();

    private static PartyResponse Party() =>
        new(Id, "Heroes", [new("A", CharacterClass.Warrior, 30, 30, false), new("B", CharacterClass.Mage, 16, 16, false)], DateTimeOffset.UtcNow);

    // ---- TypewriterText ----

    [Theory]
    [InlineData("plain", 1, false)]
    [InlineData("a **b** c", 3, true)]
    [InlineData("a ** b", 2, false)]
    public void Parse_SplitsHighlights_AndKeepsUnmatchedMarkersAsText(string text, int segments, bool anyKey)
    {
        var parsed = TypewriterText.Parse(text);

        parsed.Should().HaveCount(segments);
        parsed.Any(s => s.Key).Should().Be(anyKey);
        if (!anyKey) string.Concat(parsed.Select(s => s.Text)).Should().Be(text);
    }

    [Fact]
    public void Typewriter_Instant_RendersWholeTextWithHighlightedWords_AndCompletes()
    {
        var done = false;
        var cut = RenderComponent<TypewriterText>(p => p
            .Add(c => c.Text, "Pull the **lever** now")
            .Add(c => c.CharsPerSecond, 0)
            .Add(c => c.OnComplete, () => done = true));

        cut.Markup.Should().NotContain("tw-rest");
        cut.Find(".key").TextContent.Should().Be("lever");
        cut.Find(".typewriter").TextContent.Should().Be("Pull the lever now");
        done.Should().BeTrue();
    }

    [Fact]
    public void Typewriter_Slow_HidesUnrevealedTextKeepsItInDom_ThenClickSkips()
    {
        var done = false;
        var cut = RenderComponent<TypewriterText>(p => p
            .Add(c => c.Text, "Hello there")
            .Add(c => c.CharsPerSecond, 1)
            .Add(c => c.OnComplete, () => done = true));

        cut.Find(".typewriter").ClassList.Should().Contain("typing");
        cut.Find(".tw-rest").TextContent.Should().Contain("Hello");
        cut.Find(".typewriter").TextContent.Should().Be("Hello there");
        done.Should().BeFalse();

        cut.Find(".typewriter").Click();

        cut.FindAll(".tw-rest").Should().BeEmpty();
        done.Should().BeTrue();
    }

    [Fact]
    public void Typewriter_Hold_DoesNotStart_UntilReleased()
    {
        var cut = RenderComponent<TypewriterText>(p => p
            .Add(c => c.Text, "Wait")
            .Add(c => c.CharsPerSecond, 0)
            .Add(c => c.Hold, true));
        cut.Find(".tw-rest").TextContent.Should().Be("Wait");

        cut.SetParametersAndRender(p => p.Add(c => c.Hold, false));

        cut.FindAll(".tw-rest").Should().BeEmpty();
    }

    // ---- AsciiStage ----

    private static readonly MemberDto[] Heroes = [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)];

    [Fact]
    public void AsciiStage_DrawsTheHall_AndTheTagArtAtTheFarEnd()
    {
        var cut = RenderComponent<AsciiStage>(p => p
            .Add(c => c.Members, Heroes).Add(c => c.Room, RoomType.Combat).Add(c => c.Scene, "skeleton"));
        cut.Find("pre").GetAttribute("role").Should().Be("img");
        cut.Find("pre").TextContent.TrimEnd('\n').Split('\n').Should().HaveCount(Corridor.Height);
        cut.Find(".stage-caption").TextContent.Should().Contain("Combat");
    }

    [Fact]
    public void AsciiStage_AnimatesWhileIdle()
    {
        var cut = RenderComponent<AsciiStage>(p => p
            .Add(c => c.Members, Heroes).Add(c => c.Room, RoomType.Combat).Add(c => c.Scene, "ghost")
            .Add(c => c.Interval, TimeSpan.FromMilliseconds(20)));
        var first = cut.Find("pre").InnerHtml;

        cut.WaitForAssertion(() => cut.Find("pre").InnerHtml.Should().NotBe(first), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AsciiStage_ReportsSoundCuesWhileActingOutTheShow()
    {
        var cues = new List<string>();
        var done = new List<int>();
        var show = new StageShow(3, RoomType.Combat, "goblin",
            [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "I charge the goblin")],
            true, null, 0, 0);
        RenderComponent<AsciiStage>(p => p
            .Add(c => c.Members, Heroes).Add(c => c.Turn, 3).Add(c => c.Show, show).Add(c => c.Speed, 60)
            .Add(c => c.Interval, TimeSpan.FromMilliseconds(20))
            .Add(c => c.OnShowComplete, (int turn) => done.Add(turn))
            .Add(c => c.OnCue, (string cue) => cues.Add(cue)));

        Within(() => done.Count == 1);
        cues.Should().Equal("tick", "hit", "win");
    }

    private static void Within(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
    }

    [Fact]
    public void AsciiStage_ActsOutTheShow_ThenReportsItDone()
    {
        var done = new List<int>();
        var show = new StageShow(3, RoomType.Combat, "goblin",
            [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "I charge the goblin")],
            true, null, 0, 0);
        var cut = RenderComponent<AsciiStage>(p => p
            .Add(c => c.Members, Heroes).Add(c => c.Turn, 3).Add(c => c.Show, show).Add(c => c.Speed, 60)
            .Add(c => c.Interval, TimeSpan.FromMilliseconds(20))
            .Add(c => c.OnShowComplete, (int turn) => done.Add(turn)));

        cut.WaitForAssertion(() => done.Should().Equal(3), TimeSpan.FromSeconds(5));
        // Re-rendering with the same show does not replay it.
        cut.SetParametersAndRender(p => p.Add(c => c.Show, show));
        Thread.Sleep(150);
        done.Should().Equal(3);
    }

    [Fact]
    public void AsciiArt_EveryNarratorTagHasTwoFrames()
    {
        foreach (var tag in new[] { "goblin", "skeleton", "rat", "dragon", "ghost", "chest", "door", "lever", "trap", "altar", "generic" })
        {
            AsciiArt.Frames(tag, null).Should().HaveCount(2, tag);
            AsciiArt.Frames(tag, null)[0].Should().NotBe(AsciiArt.Frames(tag, null)[1], tag);
        }
    }

    // ---- GameSession: scene, audit scenario, my hero ----

    [Fact]
    public void Session_CarriesSceneTag_AndMovesFinishedScenarioIntoLog()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, "", 0, false, Scenario: "A **goblin** waits."));

        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, "A", 3, 0, "Clash", 0, false, NarrationPending: true));
        sut.Scenario.Should().BeNull();
        sut.Log[0].Scenario.Should().Be("A **goblin** waits.");

        sut.ApplyNarration(Id, 1, "It falls.", "A **door** looms.", RoomType.Puzzle, "door").Should().BeTrue();
        sut.Scene.Should().Be("door");
    }

    [Fact]
    public void Session_EarlyNarration_KeepsSceneTag()
    {
        var sut = new GameSession();
        sut.Start(Party());
        sut.ApplyNarration(Id, 1, "It falls.", "Next.", RoomType.Chest, "chest");

        sut.Apply(new ExploreResponse(Id, 1, RoomType.Combat, "A", 3, 0, "Clash", 0, false, NarrationPending: true));

        sut.Scene.Should().Be("chest");
    }

    [Fact]
    public void Session_Join_UsesChosenHeroForNameAndClass()
    {
        var sut = new GameSession();
        var state = new PartyStateDto(Id, "Heroes", Party().Members, DateTimeOffset.UtcNow, 0, 0, false);

        sut.Join(state, "B");

        sut.PlayerName.Should().Be("B");
        sut.PlayerClass.Should().Be(CharacterClass.Mage);
    }
}
