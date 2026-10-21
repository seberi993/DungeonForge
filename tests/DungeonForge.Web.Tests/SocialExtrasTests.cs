using System.Net;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Web.Core;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Lobby;
using DungeonForge.Web.Features.Social;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class SocialExtrasTests : TestContext
{
    private static readonly Guid Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly GameSession _session = new();
    private readonly FakeHandler _handler = new(HttpStatusCode.OK);

    public SocialExtrasTests()
    {
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false), new("Lyra", CharacterClass.Bard, 20, 20, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(_handler.CreateClient()));
    }

    private static MySocialDto Me(bool asks = true, string sent = "[]", bool optedOut = false, bool debrief = false)
    {
        var json = "{\"playerName\":\"Adventurer\",\"seeded\":true,\"finalized\":true,\"tags\":[\"bold\"],\"standing\":\"trusted\"," +
            $"\"abilities\":[],\"askTokens\":3,\"asksAvailable\":{(asks ? "true" : "false")},\"inbox\":[],\"sent\":{sent}," +
            $"\"optedOut\":{(optedOut ? "true" : "false")},\"debriefOptIn\":{(debrief ? "true" : "false")}}}";
        return JsonSerializer.Deserialize<MySocialDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static JsonElement Body(FakeHandler handler, string pathEnd) =>
        JsonDocument.Parse(handler.SocialRequests.Last(r => r.PathAndQuery.Contains(pathEnd, StringComparison.Ordinal)).Body!).RootElement;

    [Fact]
    public void ShowsTheShardsAndHowMuchYourWordIsWorth_InWordsOnly()
    {
        var cut = RenderComponent<AsksCard>(p => p.Add(c => c.Me, Me()));

        cut.Find("#ask-tokens").TextContent.Should().Be("3");
        cut.Find("#standing").TextContent.Should().Be("trusted");
    }

    [Fact]
    public void Pressing_SendsTheCompelFlag()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/asks"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var cut = RenderComponent<AsksCard>(p => p.Add(c => c.Me, Me()));

        cut.FindAll(".ask-target input").First().Change(true);
        cut.Find("#ask-text").Input("Who has the key?");
        cut.Find("#ask-compel").Change(true);
        cut.Find("#ask-send").Click();

        Body(_handler, "/social/asks").GetProperty("compel").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void AWhisperThatDrewAnswers_CanBeLaidBare()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/asks/ask_1/reveal"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var sent = "[{\"askId\":\"ask_1\",\"to\":[\"Merlin\"],\"kind\":\"question\",\"text\":\"Who has the key?\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"replies\":[{\"by\":\"Merlin\",\"summary\":\"Dru\"}]}]";
        var cut = RenderComponent<AsksCard>(p => p.Add(c => c.Me, Me(sent: sent)));

        cut.Find(".reveal-ask").Click();

        _handler.SocialRequests.Should().Contain(r => r.PathAndQuery.EndsWith("/asks/ask_1/reveal"));
        Body(_handler, "/reveal").GetProperty("from").GetString().Should().Be(_session.PlayerName);
    }

    [Fact]
    public void AWhisperWithNoAnswersYet_HasNoRevealButton()
    {
        var sent = "[{\"askId\":\"ask_1\",\"to\":[\"Merlin\"],\"kind\":\"question\",\"text\":\"Who has the key?\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"replies\":[]}]";
        var cut = RenderComponent<AsksCard>(p => p.Add(c => c.Me, Me(sent: sent)));

        cut.FindAll(".reveal-ask").Should().BeEmpty();
    }

    [Fact]
    public void Preferences_SendOnlyWhatWasToggled()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/preferences"] = (HttpStatusCode.OK, "{\"optOut\":true,\"debriefOptIn\":false}");
        var cut = RenderComponent<SocialPreferences>(p => p.Add(c => c.Me, Me()));

        cut.Find("#pref-optout").Change(true);

        var body = Body(_handler, "/social/preferences");
        body.GetProperty("optOut").GetBoolean().Should().BeTrue();
        body.TryGetProperty("debriefOptIn", out _).Should().BeFalse("unchanged settings are left out");
    }

    [Fact]
    public void TheDebrief_OptsInThenShowsTheExactProfile()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/preferences"] = (HttpStatusCode.OK, "{\"optOut\":false,\"debriefOptIn\":true}");
        _handler.SocialRoutes[$"GET /api/parties/{Id}/social/debrief"] = (HttpStatusCode.OK,
            "{\"playerName\":\"Adventurer\",\"optedIn\":true,\"axes\":{\"valor\":24,\"greed\":-9},\"tags\":[\"bold\"],\"tone\":[],\"trust\":1,\"menace\":0,\"credibility\":3," +
            "\"abilities\":[{\"name\":\"Bulwark\",\"archetype\":\"guardian\",\"rarity\":\"rare\",\"budget\":18,\"cooldown\":3,\"effects\":{\"shield\":40}}]," +
            "\"moments\":[{\"turn\":2,\"type\":\"ask_reply\",\"verdict\":\"punish\",\"rule\":\"R-THEFT\",\"rationale\":\"A lie.\",\"model\":false}],\"run\":{\"seeds\":3}}");
        var cut = RenderComponent<DebriefCard>();

        cut.Find("#debrief-open").Click();

        cut.WaitForAssertion(() => cut.Find("#debrief-axes").TextContent.Should().Contain("valor").And.Contain("24"));
        cut.Find("#debrief-abilities").TextContent.Should().Contain("Bulwark").And.Contain("shield 40");
        cut.Find("#debrief-moments").TextContent.Should().Contain("A lie.");
        Body(_handler, "/social/preferences").GetProperty("debriefOptIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ADebriefWithoutOptIn_ShowsNoNumbers()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/preferences"] = (HttpStatusCode.OK, "{\"optOut\":false,\"debriefOptIn\":true}");
        _handler.SocialRoutes[$"GET /api/parties/{Id}/social/debrief"] = (HttpStatusCode.OK,
            "{\"playerName\":\"Adventurer\",\"optedIn\":false,\"tags\":[\"bold\"],\"tone\":[],\"run\":{}}");
        var cut = RenderComponent<DebriefCard>();

        cut.Find("#debrief-open").Click();

        cut.FindAll("#debrief-axes").Should().BeEmpty();
    }

    [Fact]
    public void ARunStillInProgress_ShowsTheServersMessage()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/preferences"] = (HttpStatusCode.OK, "{\"optOut\":false,\"debriefOptIn\":true}");
        _handler.SocialRoutes[$"GET /api/parties/{Id}/social/debrief"] = (HttpStatusCode.Conflict, "{\"detail\":\"Later.\",\"code\":\"social.run_in_progress\"}");
        var cut = RenderComponent<DebriefCard>();

        cut.Find("#debrief-open").Click();

        cut.WaitForAssertion(() => cut.Find("#debrief-error").TextContent.Should().NotBeNullOrWhiteSpace());
    }
}
