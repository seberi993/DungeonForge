using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class DungeonRoomFailureTests : DungeonRoomTestBase
{
    [Fact]
    public void Explore_Conflict_ShowsDefeatedAndOffersNewParty()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Conflict));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find(".defeated").TextContent.Should().Contain("defeated"));
        cut.Find("button#explore").HasAttribute("disabled").Should().BeTrue();
        _session.IsDefeated.Should().BeTrue();
        cut.Find(".defeated .run-summary").Should().NotBeNull();
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
    public void ResolveRejectedWith400_ShowsError_AndKeepsScenario()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.BadRequest,
            """{"errors":{"actions":["At least one action is required."]}}"""));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find(".error").TextContent.Should().Contain("At least one action is required."));
        cut.Find("#scenario").Should().NotBeNull();
    }

    [Fact]
    public void Resolve_PartyDefeated_ShowsDefeatedAndDisablesExplore()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(3, "combat", "Merlin", 5, 0, "Slain", 4, defeated: true)));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("button#explore").HasAttribute("disabled").Should().BeTrue());
        cut.Find(".defeated").TextContent.Should().Contain("defeated");
        cut.Find("button#new-party").Should().NotBeNull();
    }
}
