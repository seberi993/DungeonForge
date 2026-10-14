using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class SceneTagTests
{
    [Fact]
    public void Parser_ReadsSceneTag_AndKeepsHighlightMarkers()
    {
        var result = TurnNarrationParser.Parse("""{"outcome":"The **goblin** falls.","next":"A **door** looms. What do you do?","scene":"Door"}""");

        result.Should().NotBeNull();
        result!.NextScene.Should().Be("door");
        result.Outcome.Should().Contain("**goblin**");
        result.Next.Should().Contain("**door**");
    }

    [Theory]
    [InlineData("""{"outcome":"ok","next":"n","scene":"unicorn"}""")]
    [InlineData("""{"outcome":"ok","next":"n"}""")]
    public void Parser_UnknownOrMissingScene_BecomesEmpty(string json) =>
        TurnNarrationParser.Parse(json)!.NextScene.Should().BeEmpty();

    [Fact]
    public void Prompts_AskForHighlightsAndSceneFromTheVocabulary()
    {
        var turn = NarrationPrompts.Turn(RoomType.Combat);

        turn.Should().Contain("**").And.Contain("\"scene\"");
        foreach (var tag in SceneTags.All) turn.Should().Contain(tag);
        NarrationPrompts.Scenario(RoomType.Chest).Should().Contain("**");
        NarrationPrompts.Turn(null).Should().NotContain("\"scene\"");
    }

    [Fact]
    public void Fallback_KeepsPartialScene()
    {
        var ctx = new NarrationContext(RoomType.Combat, "P", 1, false) { NextRoom = RoomType.Chest };

        FallbackFlavor.Complete(new TurnNarration("o", "n", "chest"), true, ctx).NextScene.Should().Be("chest");
    }
}
