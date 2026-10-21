using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;

namespace DungeonForge.Web.Tests;

public class ActionSuggestionsTests
{
    [Fact]
    public void Pick_IsDeterministicForASeed()
    {
        ActionSuggestions.Pick(new Random(7)).Should().Be(ActionSuggestions.Pick(new Random(7)));
    }

    [Fact]
    public void Pick_NeverNamesSpellsOrClasses()
    {
        var picks = Enumerable.Range(0, 200).Select(i => ActionSuggestions.Pick(new Random(i))).ToList();

        picks.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p) && !p.Contains("spell", StringComparison.OrdinalIgnoreCase));
    }
}
