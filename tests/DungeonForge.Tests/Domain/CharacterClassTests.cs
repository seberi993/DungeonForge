using DungeonForge.Domain;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class CharacterClassTests
{
    [Fact]
    public void Names_AllClasses_AreDefined()
    {
        Enum.GetNames<CharacterClass>().Should().BeEquivalentTo(
            "Warrior", "Mage", "Druid", "Paladin", "Priest", "Necromancer", "Bard");
    }
}
