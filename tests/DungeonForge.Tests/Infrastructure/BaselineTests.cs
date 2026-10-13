using FluentAssertions;

namespace DungeonForge.Tests.Infrastructure;

public class BaselineTests
{
    [Fact]
    public void Baseline_Passes() => true.Should().BeTrue();
}
