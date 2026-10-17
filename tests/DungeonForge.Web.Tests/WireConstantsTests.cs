using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

// Mirrors of Api wire values; DungeonForge.Tests pins the server side with the same literals.
public class WireConstantsTests
{
    [Fact]
    public void NarrationSources_MatchTheServer() =>
        (NarrationSources.Server, NarrationSources.Fallback, NarrationSources.Model).Should().Be(("server", "fallback", "model"));

    [Fact]
    public void DefaultPlayerName_MatchesTheServersDefaultActionName() => GameSession.DefaultPlayerName.Should().Be("Adventurer");
}
