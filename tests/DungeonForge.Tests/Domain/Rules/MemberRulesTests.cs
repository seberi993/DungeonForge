using DungeonForge.Domain;
using DungeonForge.Domain.Rules;
using FluentAssertions;

namespace DungeonForge.Tests.Domain.Rules;

public class MemberRulesTests
{
    [Theory]
    [InlineData(CharacterClass.Warrior, 30)]
    [InlineData(CharacterClass.Paladin, 28)]
    [InlineData(CharacterClass.Druid, 22)]
    [InlineData(CharacterClass.Priest, 20)]
    [InlineData(CharacterClass.Bard, 20)]
    [InlineData(CharacterClass.Necromancer, 18)]
    [InlineData(CharacterClass.Mage, 16)]
    public void MaxHpFor_EachClass(CharacterClass characterClass, int expected) =>
        MemberRules.MaxHpFor(characterClass).Should().Be(expected);

    [Fact]
    public void MaxHpFor_AnUnknownClass_Throws()
    {
        var act = () => MemberRules.MaxHpFor((CharacterClass)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, 1)]
    [InlineData(CharacterClass.Paladin, 1)]
    [InlineData(CharacterClass.Mage, 0)]
    [InlineData(CharacterClass.Priest, 0)]
    [InlineData(CharacterClass.Bard, 0)]
    [InlineData(CharacterClass.Druid, 0)]
    [InlineData(CharacterClass.Necromancer, 0)]
    public void Threat_OnlyFrontLinersDrawAttention(CharacterClass characterClass, int expected) =>
        MemberRules.Threat(characterClass).Should().Be(expected);
}
