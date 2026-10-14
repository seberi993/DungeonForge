using DungeonForge.Web.Components;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class ActionSuggestionsTests
{
    [Fact]
    public void Pick_IsDeterministicForASeed()
    {
        ActionSuggestions.Pick(new Random(7)).Should().Be(ActionSuggestions.Pick(new Random(7)));
    }

    [Fact]
    public void Pick_SometimesNudgesTowardTheSpellbook_NamingASpell()
    {
        var picks = Enumerable.Range(0, 200).Select(i => ActionSuggestions.Pick(new Random(i), ["Fireball"])).ToList();

        picks.Should().Contain(p => p.Contains("Fireball"));
        picks.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p));
    }

    [Fact]
    public void Pick_WithoutSpells_FallsBackToGenericSpellHint()
    {
        Enumerable.Range(0, 200).Select(i => ActionSuggestions.Pick(new Random(i)))
            .Should().Contain(ActionSuggestions.GenericSpell);
    }
}
