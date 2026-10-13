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

    private IRenderedComponent<DungeonRoom> Render(FakeHandler handler, bool withParty = true)
    {
        if (withParty) _session.Start(new PartyResponse(Id, "Heroes", ["A"], DateTimeOffset.UtcNow));
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>();
    }

    private static string ExploreJson(int turn, string room, int hpChange, int gold, string flavor, int hp, int totalGold) =>
        $$"""{"partyId":"{{Id}}","turn":{{turn}},"roomType":"{{room}}","hpChange":{{hpChange}},"goldFound":{{gold}},"flavorEvent":"{{flavor}}","hp":{{hp}},"gold":{{totalGold}}}""";

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
        cut.Find("#stat-hp").TextContent.Should().Contain(GameSession.StartingHp.ToString());
        cut.Find("#stat-gold").TextContent.Should().Contain("0");
        cut.Find("#stat-turn").TextContent.Should().Contain("0");
        var button = cut.Find("button#explore");
        button.TextContent.Should().Contain("Explore Room");
        button.HasAttribute("disabled").Should().BeFalse();
        cut.Find(".log").Should().NotBeNull();
    }

    [Fact]
    public void Explore_Success_UpdatesStatsAndAppendsLogEntry()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", -2, 15, "Shiny chest", 18, 15)));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find("#stat-hp").TextContent.Should().Contain("18");
        cut.Find("#stat-gold").TextContent.Should().Contain("15");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        var entry = cut.Find(".log-entry").TextContent;
        entry.Should().Contain("Shiny chest").And.Contain("chest").And.Contain("-2").And.Contain("15");
    }

    [Fact]
    public void Explore_Twice_AppendsEntries()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", -1, 0, "First", 19, 0)));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(2));
    }

    [Fact]
    public void Explore_HpZero_ShowsDefeatedAndDisablesExplore()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, ExploreJson(3, "combat", -5, 0, "Slain", 0, 4)));

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
}
