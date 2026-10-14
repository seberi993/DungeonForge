using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class TurnNarrationParserTests
{
    [Fact]
    public void Parse_PlainJson_ReturnsBothParts()
    {
        var result = TurnNarrationParser.Parse("{\"outcome\":\"Won.\",\"next\":\"A hall. What do you do?\"}");

        result.Should().Be(new TurnNarration("Won.", "A hall. What do you do?"));
    }

    [Fact]
    public void Parse_CodeFenceAndChatter_IsTolerated()
    {
        var result = TurnNarrationParser.Parse("Here you go:\n```json\n{\"outcome\":\"Won.\",\"next\":\"Hall.\"}\n```\nEnjoy!");

        result.Should().Be(new TurnNarration("Won.", "Hall."));
    }

    [Fact]
    public void Parse_KeysAreCaseInsensitive() =>
        TurnNarrationParser.Parse("{\"Outcome\":\"A\",\"NEXT\":\"B\"}").Should().Be(new TurnNarration("A", "B"));

    [Fact]
    public void Parse_MissingPart_YieldsEmptyForThatPartOnly()
    {
        TurnNarrationParser.Parse("{\"outcome\":\"A\"}").Should().Be(new TurnNarration("A", ""));
        TurnNarrationParser.Parse("{\"next\":\"B\"}").Should().Be(new TurnNarration("", "B"));
        TurnNarrationParser.Parse("{\"outcome\":\"A\",\"next\":null}").Should().Be(new TurnNarration("A", ""));
        TurnNarrationParser.Parse("{\"outcome\":3,\"next\":\"B\"}").Should().Be(new TurnNarration("", "B"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain prose without braces")]
    [InlineData("{broken json")]
    [InlineData("[1,2]")]
    [InlineData("{\"foo\":1}")]
    [InlineData("{\"outcome\":\" \",\"next\":\"\"}")]
    public void Parse_NothingUsable_ReturnsNull(string? text) =>
        TurnNarrationParser.Parse(text).Should().BeNull();

    [Fact]
    public void Parse_SanitizesAndCapsEachPart()
    {
        var json = "{\"outcome\":\"Line one.\\n\\nLine   two. " + new string('o', 2000)
            + "\",\"next\":\"Hall\\r\\n\\tdoor. " + new string('n', 5000) + "\"}";

        var result = TurnNarrationParser.Parse(json)!;

        result.Outcome.Should().StartWith("Line one. Line two. ");
        result.Outcome.Length.Should().BeLessThanOrEqualTo(TurnNarrationParser.MaxOutcomeLength);
        result.Next.Should().StartWith("Hall door. ");
        result.Next.Length.Should().BeLessThanOrEqualTo(TurnNarrationParser.MaxScenarioLength);
        (result.Outcome + result.Next).Should().NotContainAny("\n", "\r", "\t");
    }
}

public class FallbackFlavorTests
{
    private static readonly RoomType[] Rooms = [RoomType.Combat, RoomType.Chest, RoomType.Puzzle];

    private static int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    [Theory]
    [InlineData(RoomType.Combat)]
    [InlineData(RoomType.Chest)]
    [InlineData(RoomType.Puzzle)]
    public void Scenario_IsRichDeterministicAndEndsWithOpenQuestion(RoomType room)
    {
        for (var turn = 0; turn < 4; turn++)
        {
            var text = FallbackFlavor.Scenario(room, turn);

            Words(text).Should().BeGreaterThanOrEqualTo(60);
            text.Length.Should().BeLessThanOrEqualTo(TurnNarrationParser.MaxScenarioLength);
            text.Should().EndWith("What do you do?");
            FallbackFlavor.Scenario(room, turn).Should().Be(text);
        }
    }

    [Fact]
    public void Scenario_DiffersPerRoomType() =>
        Rooms.Select(r => FallbackFlavor.Scenario(r, 0)).Distinct().Should().HaveCount(3);

    [Fact]
    public void Scenario_NegativeTurn_DoesNotThrow() =>
        FallbackFlavor.Scenario(RoomType.Chest, -5).Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void Outcome_SuccessAndFailureDifferPerRoom()
    {
        foreach (var room in Rooms)
        {
            FallbackFlavor.Outcome(room, true, 0).Should().NotBeNullOrWhiteSpace()
                .And.NotBe(FallbackFlavor.Outcome(room, false, 0));
        }

        FallbackFlavor.Outcome(RoomType.Puzzle, true, 0).Should().NotBe(FallbackFlavor.Outcome(RoomType.Chest, true, 0));
        FallbackFlavor.Outcome(RoomType.Chest, false, -5).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Complete_NullPartial_FallsBackForBothParts()
    {
        var ctx = new NarrationContext(RoomType.Combat, "H", 2, false) { NextRoom = RoomType.Chest };

        FallbackFlavor.Complete(null, false, ctx).Should().Be(
            new TurnNarration(FallbackFlavor.Outcome(RoomType.Combat, false, 2), FallbackFlavor.Scenario(RoomType.Chest, 3)) { FromFallback = true });
    }

    [Fact]
    public void Complete_FillsOnlyBlankParts()
    {
        var ctx = new NarrationContext(RoomType.Combat, "H", 2, false) { NextRoom = RoomType.Puzzle };

        FallbackFlavor.Complete(new TurnNarration("Mine", " "), true, ctx)
            .Should().Be(new TurnNarration("Mine", FallbackFlavor.Scenario(RoomType.Puzzle, 3)) { FromFallback = true });
        FallbackFlavor.Complete(new TurnNarration("", "Mine too"), true, ctx)
            .Should().Be(new TurnNarration(FallbackFlavor.Outcome(RoomType.Combat, true, 2), "Mine too") { FromFallback = true });
        FallbackFlavor.Complete(new TurnNarration("Mine", "Mine too"), true, ctx).FromFallback.Should().BeFalse();
    }

    [Fact]
    public void Complete_WithoutNextRoom_NextStaysEmpty()
    {
        var ctx = new NarrationContext(RoomType.Combat, "H", 2, true);

        FallbackFlavor.Complete(new TurnNarration("Down.", "ignored"), false, ctx).Next.Should().BeEmpty();
        FallbackFlavor.Complete(null, false, ctx).Next.Should().BeEmpty();
    }

    [Theory]
    [InlineData("I attack the goblin", "charges in|violence")]
    [InlineData("I run away screaming", "exit|Running")]
    [InlineData("I grab the chest", "shiny|treasure")]
    [InlineData("I hum a tune", "idea|plan")]
    public void Jab_MocksTheFirstMemberByNameAndChoice(string text, string pattern)
    {
        var actions = new[] { new PlayerAction(Guid.NewGuid(), "Bork", text) };

        var jabs = Enumerable.Range(0, 2).Select(t => FallbackFlavor.Jab(actions, t)).ToList();

        jabs.Should().OnlyContain(j => j.Contains("Bork"));
        jabs.Should().Contain(j => System.Text.RegularExpressions.Regex.IsMatch(j, pattern));
    }

    [Fact]
    public void Complete_WithActions_PrefixesTheJab_AndWithoutActionsDoesNot()
    {
        var actions = new[] { new PlayerAction(Guid.NewGuid(), "Bork", "I attack") };
        var ctx = new NarrationContext(RoomType.Combat, "H", 2, false) { Actions = actions };

        var outcome = FallbackFlavor.Complete(null, true, ctx).Outcome;

        outcome.Should().StartWith(FallbackFlavor.Jab(actions, 2)).And.EndWith(FallbackFlavor.Outcome(RoomType.Combat, true, 2));
        outcome.Length.Should().BeLessThanOrEqualTo(TurnNarrationParser.MaxOutcomeLength);
        FallbackFlavor.Jab([], 2).Should().BeEmpty();
    }

    [Fact]
    public void Prompts_AskTheNarratorToMockTheParty()
    {
        NarrationPrompts.Turn(RoomType.Combat).Should().Contain("Mock the party").And.Contain("rude");
        NarrationPrompts.Turn(null).Should().Contain("Mock the party");
    }
}

public sealed class StaticDungeonNarratorTests
{
    [Theory]
    [InlineData(RoomType.Combat)]
    [InlineData(RoomType.Chest)]
    [InlineData(RoomType.Puzzle)]
    public async Task GenerateScenario_ReturnsDeterministicFallback(RoomType room)
    {
        var ctx = new NarrationContext(room, "Heroes", 4, false);

        var result = await new StaticDungeonNarrator().GenerateScenarioAsync(ctx, CancellationToken.None);

        result.Should().Be(FallbackFlavor.Scenario(room, 4));
    }

    [Theory]
    [InlineData(RoomType.Combat, true)]
    [InlineData(RoomType.Chest, false)]
    [InlineData(RoomType.Puzzle, true)]
    public async Task NarrateTurn_ReturnsFallbackOutcomeAndNextScenario(RoomType room, bool success)
    {
        var ctx = new NarrationContext(room, "Heroes", 4, false) { NextRoom = RoomType.Chest };

        var result = await new StaticDungeonNarrator().NarrateTurnAsync(success, ctx, CancellationToken.None);

        result.Should().Be(new TurnNarration(FallbackFlavor.Outcome(room, success, 4), FallbackFlavor.Scenario(RoomType.Chest, 5)) { FromFallback = true });
    }
}
