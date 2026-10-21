using System.Net;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class DungeonRoomHubTests : DungeonRoomTestBase
{
    [Fact]
    public void RemoteResolvedTurn_ClearsScenario_AndShowsPlaceholder()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Remote", 7)));

        cut.WaitForAssertion(() => cut.FindAll("#scenario").Should().BeEmpty());
        cut.Find("#scenario-pending").Should().NotBeNull();
    }

    [Fact]
    public void Connects_HubForParty()
    {
        Render(new FakeHandler(HttpStatusCode.OK));
        _hub.ConnectedParty.Should().Be(Id);
    }

    [Fact]
    public void RemoteTurn_UpdatesLogAndStats()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Remote loot", 7)));

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find("#stat-gold").TextContent.Should().Contain("7");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find(".log-entry").TextContent.Should().Contain("Remote loot");
    }

    [Fact]
    public void DuplicateTurn_IsNotAppliedTwice()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", null, 0, 7, "Mine", 7)));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Mine", 7)));

        cut.FindAll(".log-entry").Should().HaveCount(1);
    }

    [Fact]
    public void RemoteTurn_PartyDefeated_ShowsDefeated()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));
        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Combat, "Conan", 20, 0, "Slain", 0, defeated: true)));
        cut.WaitForAssertion(() => cut.Find(".defeated"));
        cut.Find("button#explore").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ConnectFailure_ShowsOfflineNoticeAndExploreStillWorks()
    {
        _hub = new FakeDungeonHubClient { FailConnect = true };
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 1, 0, "Hit", 0)));

        cut.WaitForAssertion(() => cut.Find(".offline").TextContent.Should().Contain("offline"));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
    }

    [Fact]
    public async Task Dispose_DisposesHubClient()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        _hub.Disposed.Should().BeTrue();
    }
}
