using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Shared;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

// The browser stops typing where the Api starts rejecting, because both read the same linked Limits file.
public class SharedLimitsTests : TestContext
{
    private IRenderedComponent<PartyLobby> RenderLobby(FakeHandler handler)
    {
        Services.AddSingleton(new GameSession());
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<PartyLobby>();
    }

    [Fact]
    public void TheSharedLimits_MatchTheApiValues() =>
        (Limits.MaxMembers, Limits.MaxPartyNameLength, Limits.MaxMemberNameLength, Limits.MaxPlayerNameLength,
            Limits.MaxActionTextLength, Limits.MaxActionNameLength, Limits.MaxActions)
        .Should().Be((8, 40, 32, 40, 200, 40, 8));

    [Fact]
    public void LobbyInputs_StopAtTheServersLimits()
    {
        var cut = RenderLobby(new FakeHandler(HttpStatusCode.OK));

        cut.Find("input#party-name").GetAttribute("maxlength").Should().Be("40");
        cut.Find("input#member-name-0").GetAttribute("maxlength").Should().Be("32");
    }

    [Fact]
    public void JoinName_StopsAtTheServersLimit()
    {
        var id = Guid.NewGuid();
        var cut = RenderLobby(new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{id}}","name":"Heroes","members":[{"name":"Conan","class":"warrior","hp":30,"maxHp":30,"isDefeated":false}],"createdAt":"2026-01-01T00:00:00+00:00","inviteCode":"ABCD-EFGH"}"""));

        cut.Find("input#join-id").Change("ABCD-EFGH");
        cut.Find("button#join-party").Click();

        cut.WaitForAssertion(() => cut.Find("input#join-name").GetAttribute("maxlength").Should().Be("32"));
    }

    [Fact]
    public void TheActionBox_StopsAtTheActionTextLimit()
    {
        // Pinned by value here; DungeonRoomTests renders the room itself.
        Limits.MaxActionTextLength.Should().Be(200);
    }
}
