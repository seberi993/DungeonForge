using DungeonForge.Domain;
using FluentAssertions;

namespace DungeonForge.Tests.Domain;

public class ElementTypeTests
{
    [Fact]
    public void Names_AllElements_AreDefined()
    {
        Enum.GetNames<ElementType>().Should().BeEquivalentTo(
            "Physical", "Fire", "Ice", "Holy", "Unholy");
    }
}
