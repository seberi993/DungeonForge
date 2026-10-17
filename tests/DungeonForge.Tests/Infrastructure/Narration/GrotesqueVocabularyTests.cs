using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class GrotesqueVocabularyTests
{
    [Fact]
    public void Pool_IsLargeDistinctAndFreeOfForbiddenTropes()
    {
        GrotesqueVocabulary.Elements.Count.Should().BeGreaterThanOrEqualTo(20);
        GrotesqueVocabulary.Elements.Should().OnlyHaveUniqueItems();
        foreach (var element in GrotesqueVocabulary.Elements)
        {
            foreach (var trope in GrotesqueVocabulary.ForbiddenTropes)
            {
                element.Should().NotContainEquivalentOf(trope);
            }
        }
    }

    [Fact]
    public void ForbiddenTropes_CoverTheKnownClichesAndPoolHasSignatureElements()
    {
        GrotesqueVocabulary.ForbiddenTropes.Should().Contain(["torch", "flickering candle", "dusty chest", "cobwebs"]);
        GrotesqueVocabulary.Elements.Should().Contain(e => e.Contains("obelisk"))
            .And.Contain(e => e.Contains("mosaic"))
            .And.Contain(e => e.Contains("mercury"));
    }

    [Fact]
    public void Pick_IsDeterministicForTheSameRollerScript()
    {
        var a = new VocabularySelector(new ScriptedRoller(3, 9, 14, 1, 7)).Pick(2);
        var b = new VocabularySelector(new ScriptedRoller(3, 9, 14, 1, 7)).Pick(2);

        a.Should().Equal(b);
        a.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Pick_RotatesAndAvoidsRepeatsAcrossConsecutivePrompts_EvenWithAStuckRoller()
    {
        var selector = new VocabularySelector(new ScriptedRoller(0));
        var previous = new HashSet<string>();
        for (var i = 0; i < 8; i++)
        {
            var picked = selector.Pick(2);
            picked.Should().OnlyHaveUniqueItems();
            picked.Should().NotIntersectWith(previous);
            previous = [.. picked];
        }
    }

    [Fact]
    public void Pick_WithRandomRoller_VariesAcrossCalls()
    {
        var selector = new VocabularySelector(new RandomRoller());
        var seen = Enumerable.Range(0, 20).SelectMany(_ => selector.Pick(2)).Distinct().Count();

        seen.Should().BeGreaterThan(10);
    }
}
