using Bunit;
using FluentAssertions;
using DungeonForge.Web.Features.Dungeon;

namespace DungeonForge.Web.Tests;

public class PartyLockStatusTests : TestContext
{
    [Fact]
    public void ShowsCountAndPerMemberState()
    {
        var cut = RenderComponent<PartyLockStatus>(p => p.Add(c => c.Status,
            [new LockStatusDto("Conan", true), new LockStatusDto("Merlin", false), new LockStatusDto("Lyra", true)]));

        cut.Find(".party-lock-status .lock-summary").TextContent.Should().Contain("Waiting for Party Members").And.Contain("(2/3)");
        var items = cut.FindAll(".lock-member").ToList();
        items.Should().HaveCount(3);
        items[0].ClassList.Should().Contain("locked");
        items[0].TextContent.Should().Contain("Conan");
        items[1].ClassList.Should().Contain("waiting");
        items[1].TextContent.Should().Contain("Merlin");
    }

    [Fact]
    public void Empty_RendersNothing()
    {
        var cut = RenderComponent<PartyLockStatus>(p => p.Add(c => c.Status, []));
        cut.FindAll(".party-lock-status").Should().BeEmpty();
    }

    [Fact]
    public void NobodyLocked_ShowsZeroCount()
    {
        var cut = RenderComponent<PartyLockStatus>(p => p.Add(c => c.Status, [new LockStatusDto("A", false), new LockStatusDto("B", false)]));
        cut.Find(".lock-summary").TextContent.Should().Contain("(0/2)");
    }
}
