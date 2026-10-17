using DungeonForge.Api.Features.Parties.ExploreDungeon;
using FluentAssertions;

namespace DungeonForge.Tests.Features.Parties;

// These strings are part of the wire contract (NarrationBrief.Kind, NarrationReady.Source, the default action name), and the
// Web client mirrors them (DungeonForge.Web.Tests pins the same values). Renaming one is a protocol change.
public class WireConstantsTests
{
    [Fact]
    public void NarrationSources_AreTheValuesTheClientCompares()
    {
        (NarrationSources.Server, NarrationSources.Fallback, NarrationSources.Model).Should().Be(("server", "fallback", "model"));
    }

    [Fact]
    public void NarrationKinds_AreTheValuesTheClientSwitchesOn()
    {
        (NarrationKinds.Scenario, NarrationKinds.Turn).Should().Be(("scenario", "turn"));
    }

    [Fact]
    public void DefaultActionName_IsAdventurer() => ExploreRequestLimits.DefaultActionName.Should().Be("Adventurer");
}
