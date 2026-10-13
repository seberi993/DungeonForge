using DungeonForge.Api.Domain;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class PartyMemberTests
{
    [Theory]
    [InlineData(CharacterClass.Warrior, 30)]
    [InlineData(CharacterClass.Paladin, 28)]
    [InlineData(CharacterClass.Druid, 22)]
    [InlineData(CharacterClass.Priest, 20)]
    [InlineData(CharacterClass.Bard, 20)]
    [InlineData(CharacterClass.Necromancer, 18)]
    [InlineData(CharacterClass.Mage, 16)]
    public void Ctor_Class_StartsAtClassMaxHp(CharacterClass cls, int expectedMaxHp)
    {
        var member = new PartyMember("Hero", cls);

        member.Name.Should().Be("Hero");
        member.Class.Should().Be(cls);
        member.MaxHp.Should().Be(expectedMaxHp);
        member.Hp.Should().Be(expectedMaxHp);
        member.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void TakeDamage_PositiveAmount_ReducesHpAndReturnsApplied()
    {
        var member = new PartyMember("Hero", CharacterClass.Warrior);

        var applied = member.TakeDamage(7);

        applied.Should().Be(7);
        member.Hp.Should().Be(23);
    }

    [Fact]
    public void TakeDamage_MoreThanHp_ClampsAtZeroAndReturnsClampedAmount()
    {
        var member = new PartyMember("Hero", CharacterClass.Mage);
        member.TakeDamage(10);

        var applied = member.TakeDamage(50);

        applied.Should().Be(6);
        member.Hp.Should().Be(0);
    }

    [Fact]
    public void IsDefeated_HpReachesZero_IsTrue()
    {
        var member = new PartyMember("Hero", CharacterClass.Mage);

        member.TakeDamage(16);

        member.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void IsDefeated_HpAboveZero_IsFalse()
    {
        var member = new PartyMember("Hero", CharacterClass.Mage);

        member.TakeDamage(15);

        member.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void Heal_WoundedMember_RestoresHpAndReturnsApplied()
    {
        var member = new PartyMember("Hero", CharacterClass.Priest);
        member.TakeDamage(10);

        var healed = member.Heal(4);

        healed.Should().Be(4);
        member.Hp.Should().Be(14);
    }

    [Fact]
    public void Heal_MoreThanMissing_ClampsAtMaxHp()
    {
        var member = new PartyMember("Hero", CharacterClass.Priest);
        member.TakeDamage(3);

        var healed = member.Heal(100);

        healed.Should().Be(3);
        member.Hp.Should().Be(member.MaxHp);
    }

    [Fact]
    public void Heal_DefeatedMember_DoesNothing()
    {
        var member = new PartyMember("Hero", CharacterClass.Priest);
        member.TakeDamage(100);

        var healed = member.Heal(10);

        healed.Should().Be(0);
        member.Hp.Should().Be(0);
        member.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void TakeDamage_NegativeAmount_Throws()
    {
        var member = new PartyMember("Hero", CharacterClass.Warrior);

        var act = () => member.TakeDamage(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
        member.Hp.Should().Be(member.MaxHp);
    }

    [Fact]
    public void Heal_NegativeAmount_Throws()
    {
        var member = new PartyMember("Hero", CharacterClass.Warrior);
        member.TakeDamage(5);

        var act = () => member.Heal(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
        member.Hp.Should().Be(25);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Ctor_BlankName_Throws(string? name)
    {
        var act = () => new PartyMember(name!, CharacterClass.Mage);

        act.Should().Throw<ArgumentException>();
    }
}
