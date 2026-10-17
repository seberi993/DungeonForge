using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

// The turn math for rooms, written out by hand. ExploreDungeonRulesTests pins the same numbers through the endpoint.
public class RoomRulesTests
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
    // Combat 6 + turn % 3 + turn / 3 (floor does not matter)
    [InlineData(RoomType.Combat, 0, 1, 6)]
    [InlineData(RoomType.Combat, 2, 1, 8)]
    [InlineData(RoomType.Combat, 3, 1, 7)]
    [InlineData(RoomType.Combat, 8, 2, 10)]
    // Puzzle 7 + turn % 3 + turn / 3
    [InlineData(RoomType.Puzzle, 0, 1, 7)]
    [InlineData(RoomType.Puzzle, 2, 1, 9)]
    [InlineData(RoomType.Puzzle, 3, 3, 8)]
    [InlineData(RoomType.Puzzle, 8, 2, 11)]
    // Chest 4 + turn / 3
    [InlineData(RoomType.Chest, 2, 1, 4)]
    [InlineData(RoomType.Chest, 3, 1, 5)]
    [InlineData(RoomType.Chest, 8, 2, 6)]
    // Boss 8 + 2 * (floor - 1) + turn % 3: it grows with the floor, not with the turn count
    [InlineData(RoomType.Boss, 4, 1, 9)]
    [InlineData(RoomType.Boss, 9, 2, 10)]
    [InlineData(RoomType.Boss, 15, 3, 12)]
    [InlineData(RoomType.Boss, 100, 1, 9)]
    public void FailDamage_ByRoomTurnAndFloor(RoomType room, int turn, int floor, int expected) =>
        RoomRules.FailDamage(room, turn, floor).Should().Be(expected);

    [Theory]
    // Chest 5 + turn, puzzle 10 + 2 * turn, combat pays nothing, boss pays 25 * floor
    [InlineData(RoomType.Chest, 0, 1, 5)]
    [InlineData(RoomType.Chest, 8, 2, 13)]
    [InlineData(RoomType.Puzzle, 0, 1, 10)]
    [InlineData(RoomType.Puzzle, 8, 2, 26)]
    [InlineData(RoomType.Combat, 5, 1, 0)]
    [InlineData(RoomType.Boss, 4, 1, 25)]
    [InlineData(RoomType.Boss, 9, 2, 50)]
    [InlineData(RoomType.Boss, 30, 4, 100)]
    public void Gold_ByRoomTurnAndFloor(RoomType room, int turn, int floor, int expected) =>
        RoomRules.Gold(room, turn, floor).Should().Be(expected);

    [Theory]
    [InlineData(16, 16, false)]
    [InlineData(9, 16, false)]
    [InlineData(8, 16, true)]
    [InlineData(0, 16, true)]
    [InlineData(32, 62, false)]
    [InlineData(31, 62, true)]
    public void IsWounded_IsAtOrBelowHalfOfMaxHp(int hp, int maxHp, bool expected) =>
        RoomRules.IsWounded(hp, maxHp).Should().Be(expected);

    [Fact]
    public void Rollable_IsCombatChestAndPuzzle_InTheOrderTheRollerIndexes()
    {
        RoomRules.Rollable.Should().Equal(RoomType.Combat, RoomType.Chest, RoomType.Puzzle);
        RoomRules.Rollable.Should().NotContain(RoomType.Boss);
    }

    [Theory]
    [InlineData(0, RoomType.Combat)]
    [InlineData(1, RoomType.Chest)]
    [InlineData(2, RoomType.Puzzle)]
    public void Roll_MapsTheRollerResultToARoom_WithExactlyOneCallOfMaxThree(int value, RoomType expected)
    {
        var roller = new CountingRoller(value);

        RoomRules.Roll(roller).Should().Be(expected);

        roller.Maxes.Should().Equal(3);
    }
}
