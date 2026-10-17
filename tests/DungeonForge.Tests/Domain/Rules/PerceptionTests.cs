using DungeonForge.Api.Domain;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Domain.Rules;

public class PerceptionTests
{
    [Fact]
    public void BardAndDruid_SeeMoreThanTheRest()
    {
        Perception.ChancePercent(CharacterClass.Bard).Should().BeGreaterThan(Perception.ChancePercent(CharacterClass.Warrior));
        Perception.ChancePercent(CharacterClass.Druid).Should().Be(Perception.ChancePercent(CharacterClass.Bard));
        Perception.ChancePercent(CharacterClass.Warrior).Should().Be(Perception.BasePercent);
    }

    [Fact]
    public void Spotters_AreThoseWhoseRollBeatsTheirChance()
    {
        // Roll 30: the warrior (15%) misses, the bard (40%) catches.
        var observers = new PartyMember[] { new("Conan", CharacterClass.Warrior), new("Lyra", CharacterClass.Bard) };

        Perception.Spotters(observers, new ScriptedRoller(30)).Should().Equal("Lyra");
        Perception.Spotters(observers, new ScriptedRoller(50)).Should().BeEmpty();
        Perception.Spotters(observers, new ScriptedRoller(0)).Should().Equal("Conan", "Lyra");
    }
}
