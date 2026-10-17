using DungeonForge.Shared;
using FluentAssertions;

namespace DungeonForge.Tests;

// The linked Limits file is compiled into the Api. DungeonForge.Web.Tests pins the same values for the browser's copy.
public class LimitsTests
{
    [Fact]
    public void TheSharedLimits_AreTheValuesTheApiHasAlwaysEnforced() =>
        (Limits.MaxMembers, Limits.MaxPartyNameLength, Limits.MaxMemberNameLength, Limits.MaxPlayerNameLength,
            Limits.MaxActionTextLength, Limits.MaxActionNameLength, Limits.MaxActions)
        .Should().Be((8, 40, 32, 40, 200, 40, 8));
}
