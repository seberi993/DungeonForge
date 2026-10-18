using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Tests.Support;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class StoryLeanTests
{
    private sealed class CountingRoller(int value) : IRoller
    {
        public List<int> Maxes { get; } = [];

        public int Next(int max)
        {
            Maxes.Add(max);
            return value;
        }
    }

    [Theory]
    [InlineData("We charge in and fight the beast", 2, 0, 0)]
    [InlineData("I pocket the gold and loot the coins", 0, 4, 0)]
    [InlineData("Read the ancient runes for a clue", 0, 0, 4)]
    [InlineData("We wait and breathe", 0, 0, 0)]
    public void Score_CountsKeywordsPerRoomKind(string text, int combat, int chest, int puzzle) =>
        StoryTags.Score(text).Should().Be(new StoryLean(combat, chest, puzzle));

    [Fact]
    public void Favored_IsTheStrictLeader_AndNullOnATieOrNothing()
    {
        new StoryLean(3, 1, 0).Favored.Should().Be(RoomType.Combat);
        new StoryLean(0, 0, 2).Favored.Should().Be(RoomType.Puzzle);
        new StoryLean(2, 2, 0).Favored.Should().BeNull();
        default(StoryLean).Favored.Should().BeNull();
    }

    [Theory]
    [InlineData(3, 1, 0, 20)]
    [InlineData(1, 0, 0, 10)]
    [InlineData(9, 0, 0, 40)]
    [InlineData(2, 2, 0, 0)]
    public void SwapPercent_GrowsWithTheMargin_AndIsCappedAtForty(int combat, int chest, int puzzle, int expected) =>
        new StoryLean(combat, chest, puzzle).SwapPercent.Should().Be(expected);

    [Fact]
    public void Roll_WithNoLean_IsOneCallOfThree()
    {
        var roller = new CountingRoller(1);

        RoomRules.Roll(roller, default).Should().Be(RoomType.Chest);

        roller.Maxes.Should().Equal(3);
    }

    [Fact]
    public void Roll_WithLean_AppendsOneHundredSidedCall_AndSwapsUnderThePercent()
    {
        var lean = new StoryLean(3, 1, 0); // favors combat, 20%
        var swapped = new ScriptedRoller(2, 19);
        var kept = new ScriptedRoller(2, 20);

        RoomRules.Roll(swapped, lean).Should().Be(RoomType.Combat);
        RoomRules.Roll(kept, lean).Should().Be(RoomType.Puzzle);
    }

    [Fact]
    public void Roll_WithLean_UsesBoundsThreeThenOneHundred()
    {
        var roller = new CountingRoller(0);

        RoomRules.Roll(roller, new StoryLean(0, 4, 0));

        roller.Maxes.Should().Equal(3, 100);
    }
}
