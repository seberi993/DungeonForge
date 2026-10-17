using DungeonForge.Api.Domain.Rules;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class PartyRulesTests
{
    [Fact]
    public void Limits_AreEightMembersAndEightDiscussionEntries()
    {
        PartyRules.MaxMembers.Should().Be(8);
        PartyRules.MaxDiscussionEntries.Should().Be(8);
    }

    [Fact]
    public void AFloor_IsFourOrdinaryRooms()
    {
        PartyRules.RoomsPerFloor.Should().Be(4);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 40)]
    [InlineData(3, 50)]
    [InlineData(5, 70)]
    public void BossHpFor_StartsAtThirtyAndGainsTenPerFloor(int floor, int expected) =>
        PartyRules.BossHpFor(floor).Should().Be(expected);

    [Theory]
    [InlineData(30, 12)]
    [InlineData(28, 12)]
    [InlineData(22, 9)]
    [InlineData(20, 8)]
    [InlineData(18, 8)]
    [InlineData(16, 7)]
    public void FloorHealFor_IsFortyPercentOfMaxHp_RoundedUp(int maxHp, int expected) =>
        PartyRules.FloorHealFor(maxHp).Should().Be(expected);
}
