using DungeonForge.Api.Domain;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class NarrationBrevityTests
{
    [Fact]
    public void CleanSentences_CutsAtTheLastFullSentence()
    {
        var text = "One short sentence here. Another sentence that runs well past the limit of the budget";

        NarrationText.CleanSentences(text, 40).Should().Be("One short sentence here.");
    }

    [Fact]
    public void RunFlavor_ThemeIsStablePerParty_ButVariesAcrossParties()
    {
        RunFlavor.Theme("The Brave").Should().Be(RunFlavor.Theme("The Brave"));
        Enumerable.Range(0, 20).Select(i => RunFlavor.Theme($"party {i}")).Distinct().Count().Should().BeGreaterThan(3);
    }

    [Fact]
    public void FallbackScenarios_AreVariedAcrossTurns()
    {
        foreach (var room in Enum.GetValues<RoomType>())
        {
            Enumerable.Range(0, 5).Select(t => FallbackFlavor.Scenario(room, t)).Distinct().Should().HaveCount(5);
        }
    }
}
