using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Web.Models;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class DungeonRoomTests : TestContext
{
    private readonly GameSession _session = new();
    private FakeNavigationManager Nav => Services.GetRequiredService<FakeNavigationManager>();
    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DungeonRoomTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private FakeDungeonHubClient _hub = new();

    private IRenderedComponent<DungeonRoom> Render(FakeHandler handler, bool withParty = true)
    {
        Services.AddSingleton<IDungeonHubClient>(_hub);
        if (withParty) _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>();
    }

    private static string ExploreJson(int turn, string room, string? target, int damage, int gold, string flavor, int totalGold, bool defeated = false) =>
        $$"""{"partyId":"{{Id}}","turn":{{turn}},"roomType":"{{room}}","targetName":{{(target is null ? "null" : $"\"{target}\"")}},"damage":{{damage}},"goldFound":{{gold}},"flavorEvent":"{{flavor}}","gold":{{totalGold}},"isPartyDefeated":{{(defeated ? "true" : "false")}}}""";

    private static ExploreResponse Turn(int turn, RoomType room, string? target, int damage, int gold, string flavor, int totalGold, bool defeated = false) =>
        new(Id, turn, room, target, damage, gold, flavor, totalGold, defeated);

    [Fact]
    public void NoSession_RedirectsToLobby()
    {
        Render(new FakeHandler(HttpStatusCode.OK), withParty: false);
        new Uri(Nav.Uri).AbsolutePath.Should().Be("/");
    }

    [Fact]
    public void Renders_StatsAndEnabledExploreButton()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));
        cut.Find("#stat-name").TextContent.Should().Contain("Heroes");
        cut.Find("#stat-hp").TextContent.Should().Contain("46/46");
        cut.FindAll("#members li").Should().HaveCount(2);
        cut.Find("#member-0").TextContent.Should().Contain("Conan").And.Contain("30/30");
        cut.Find("#member-1").TextContent.Should().Contain("Merlin").And.Contain("16/16");
        cut.Find("#stat-gold").TextContent.Should().Contain("0");
        cut.Find("#stat-turn").TextContent.Should().Contain("0");
        var button = cut.Find("button#explore");
        button.TextContent.Should().Contain("Explore Room");
        button.HasAttribute("disabled").Should().BeFalse();
        cut.Find(".log").Should().NotBeNull();
    }

    [Fact]
    public void ShowsInviteCode_AndCopyButtonCopiesPartyId()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));
        cut.Find("#invite-code").GetAttribute("value").Should().Be("K7F2-9QXM");

        var invocation = JSInterop.SetupVoid("dungeonForge.copyText", "K7F2-9QXM");
        invocation.SetVoidResult();
        cut.Find("#copy-invite").Click();

        invocation.Invocations.Should().ContainSingle();
        cut.Find("#copy-invite").TextContent.Should().Be("Copied!");
    }

    [Fact]
    public void Explore_Success_UpdatesStatsAndAppendsLogEntry()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", null, 0, 15, "Shiny chest", 15)));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
                cut.Find("#stat-gold").TextContent.Should().Contain("15");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        var entry = cut.Find(".log-entry").TextContent;
        entry.Should().Contain("Shiny chest").And.Contain("chest").And.Contain("15");
    }

    [Fact]
    public void Explore_Combat_ReducesTargetMemberHpAndLogsDamage()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 4, 0, "Clash", 0)));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("#member-0").TextContent.Should().Contain("26/30"));
        cut.Find("#member-1").TextContent.Should().Contain("16/16");
        cut.Find("#stat-hp").TextContent.Should().Contain("42/46");
        cut.Find(".log-entry").TextContent.Should().Contain("Conan took 4 damage");
    }

    [Fact]
    public void Explore_Twice_AppendsEntries()
    {
        var turn = 0;
        var cut = Render(new FakeHandler(HttpStatusCode.OK, bodyFactory: () => ExploreJson(++turn, "combat", "Conan", 1, 0, "Step", 0)));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(2));
    }

    [Fact]
    public void BroadcastThenHttp_SameTurn_LogsOnce()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", null, 0, 7, "Same", 7)));
        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Same", 7)));
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse());
        cut.FindAll(".log-entry").Should().HaveCount(1);
    }

    [Fact]
    public void StaleHttpTurn_DoesNotRegressState()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 9, 0, "Old", 0)));
        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(2, RoomType.Chest, null, 0, 5, "New", 5)));
        cut.WaitForAssertion(() => cut.Find("#stat-turn").TextContent.Should().Contain("2"));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse());
        _session.Turn.Should().Be(2);
        _session.TotalHp.Should().Be(46);
        cut.FindAll(".log-entry").Should().HaveCount(1);
    }

    [Fact]
    public void Explore_PartyDefeated_ShowsDefeatedAndDisablesExplore()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(3, "combat", "Merlin", 5, 0, "Slain", 4, defeated: true)));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("button#explore").HasAttribute("disabled").Should().BeTrue());
        cut.Find(".defeated").TextContent.Should().Contain("defeated");
        cut.Find("button#new-party").Should().NotBeNull();
    }

    [Fact]
    public void Explore_Conflict_ShowsDefeatedAndOffersNewParty()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Conflict));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find(".defeated").TextContent.Should().Contain("defeated"));
        cut.Find("button#explore").HasAttribute("disabled").Should().BeTrue();
        _session.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void NewParty_ClearsSessionAndNavigatesToLobby()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Conflict));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.Find("button#new-party"));

        cut.Find("button#new-party").Click();

        _session.HasParty.Should().BeFalse();
        new Uri(Nav.Uri).AbsolutePath.Should().Be("/");
    }

    [Fact]
    public void Explore_NotFound_ClearsSessionAndReturnsToLobby()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.NotFound));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => new Uri(Nav.Uri).AbsolutePath.Should().Be("/"));
        _session.HasParty.Should().BeFalse();
    }

    [Fact]
    public void Explore_ServerError_ShowsErrorAndKeepsExploreEnabled()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.InternalServerError));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find(".error").TextContent.Should().NotBeNullOrWhiteSpace());
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
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
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", null, 0, 7, "Mine", 7)));
        cut.Find("button#explore").Click();
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
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 1, 0, "Hit", 0)));

        cut.WaitForAssertion(() => cut.Find(".offline").TextContent.Should().Contain("offline"));
        cut.Find("button#explore").Click();
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
