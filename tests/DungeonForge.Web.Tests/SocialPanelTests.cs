using System.Net;
using System.Text.Json;
using Bunit;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class SocialPanelTests : TestContext
{
    private static readonly Guid Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly GameSession _session = new();
    private readonly FakeDungeonHubClient _hub = new();
    private readonly FakeHandler _handler = new(HttpStatusCode.OK);

    private string Me { get; set; } = "{}";

    public SocialPanelTests()
    {
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false), new("Lyra", CharacterClass.Bard, 20, 20, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
    }

    private static string MeJson(
        string? seedQuestion = null, bool seeded = true, bool finalized = true, string abilities = "[]", bool asks = false, string inbox = "[]",
        string sent = "[]", string tags = "[\"bold\"]") =>
        "{\"playerName\":\"Adventurer\"," + (seedQuestion is null ? "" : $"\"seedQuestion\":\"{seedQuestion}\",") +
        $"\"seeded\":{(seeded ? "true" : "false")},\"finalized\":{(finalized ? "true" : "false")},\"tags\":{tags},\"standing\":\"steady\"," +
        $"\"abilities\":{abilities},\"askTokens\":3,\"asksAvailable\":{(asks ? "true" : "false")},\"inbox\":{inbox},\"sent\":{sent}}}";

    private IRenderedComponent<SocialPanel> Render(string me, string? peerPrompt = null)
    {
        _handler.SocialRoutes[$"GET /api/parties/{Id}/social/me"] = (HttpStatusCode.OK, me);
        if (peerPrompt is not null) _handler.SocialRoutes[$"GET /api/parties/{Id}/social/peer-prompt"] = (HttpStatusCode.OK, peerPrompt);
        Services.AddSingleton<IDungeonHubClient>(_hub);
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(_handler.CreateClient()));
        return RenderComponent<SocialPanel>();
    }

    private const string Prompt = "{\"promptId\":\"pr_1\",\"question\":\"Would Merlin hold the door?\",\"about\":\"Merlin\",\"options\":[\"yes\",\"maybe\",\"no\"],\"remaining\":2}";
    private const string Gift = "{\"id\":\"ab_1\",\"name\":\"Bulwark\",\"flavor\":\"A wall of will.\",\"hint\":\"Turns blows aside; slow to return.\",\"ready\":true}";
    private const string Tired = "{\"id\":\"ab_2\",\"name\":\"Lull\",\"flavor\":\"Brief calm.\",\"hint\":\"Turns blows aside; steady to return.\",\"ready\":false}";

    [Fact]
    public void WhenTheServerHasSocialOff_NothingIsRendered()
    {
        _handler.SocialRoutes.Clear();
        Services.AddSingleton<IDungeonHubClient>(_hub);
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(_handler.CreateClient()));

        var cut = RenderComponent<SocialPanel>();

        cut.FindAll("#social").Should().BeEmpty();
    }

    [Fact]
    public void UnseededPlayer_SeesTheDoorsQuestion_AndSubmitsTheirAnswer()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/seed"] = (HttpStatusCode.OK, "{\"playerName\":\"Adventurer\",\"tags\":[\"bold\"],\"finalized\":false,\"waiting\":2}");
        var cut = Render(MeJson("What do you do when no one is watching?", seeded: false, finalized: false));

        cut.Find(".social-prompt").TextContent.Should().Contain("no one is watching");
        cut.Find("#seed-submit").HasAttribute("disabled").Should().BeTrue("an empty answer cannot be sent");
        cut.Find("#seed-answer").Input("I would fight at the door");
        cut.Find("#seed-submit").Click();

        var post = _handler.SocialRequests.Single(r => r.Method == HttpMethod.Post);
        post.PathAndQuery.Should().EndWith("/social/seed");
        JsonDocument.Parse(post.Body!).RootElement.GetProperty("answer").GetString().Should().Be("I would fight at the door");
    }

    [Fact]
    public void ASeededPlayerWhoWaits_CanStartWithoutTheOthers()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/finalize"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var cut = Render(MeJson(seeded: true, finalized: false));

        cut.Find("#seed-waiting").TextContent.Should().Contain("Waiting");
        cut.Find("#seed-start").Click();

        _handler.SocialRequests.Should().Contain(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/social/finalize"));
    }

    [Fact]
    public void ThePeerPrompt_OffersYesMaybeNo_AndSendsTheChoice()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/peer-answer"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var cut = Render(MeJson(), Prompt);

        cut.Find("#peer-prompt .social-prompt").TextContent.Should().Contain("Merlin");
        cut.FindAll(".peer-choice").Select(b => b.TextContent.Trim()).Should().Equal("Yes", "Maybe", "No");
        cut.Find(".peer-yes").Click();

        var post = _handler.SocialRequests.Single(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(post.Body!).RootElement;
        body.GetProperty("promptId").GetString().Should().Be("pr_1");
        body.GetProperty("choice").GetString().Should().Be("yes");
    }

    [Fact]
    public void Gifts_ShowFlavorAndAHint_NeverANumber_AndRecoveringOnesCannotBePicked()
    {
        var cut = Render(MeJson(abilities: $"[{Gift},{Tired}]"));

        var items = cut.FindAll(".ability").ToList();
        items.Should().HaveCount(2);
        items[0].TextContent.Should().Contain("Bulwark").And.Contain("A wall of will.").And.Contain("Turns blows aside");
        items[1].QuerySelector(".ability-pick")!.HasAttribute("disabled").Should().BeTrue();
        items[1].TextContent.Should().Contain("Recovering");
        cut.Find("#abilities").TextContent.Should().NotMatchRegex("[0-9]");
    }

    [Fact]
    public void PickingAGift_SelectsItForTheTurn_AndPickingItAgainClearsIt()
    {
        var cut = Render(MeJson(abilities: $"[{Gift}]"));

        cut.Find(".ability-pick").Click();
        _session.SelectedAbilityId.Should().Be("ab_1");
        cut.Find(".ability").ClassList.Should().Contain("chosen");

        cut.Find(".ability-pick").Click();
        _session.SelectedAbilityId.Should().BeNull();
    }

    [Fact]
    public void Whispers_AreHidden_WhenThePartyIsTooSmall()
    {
        var cut = Render(MeJson(asks: false));

        cut.FindAll("#asks").Should().BeEmpty();
    }

    [Fact]
    public void TheComposer_OffersOnlyTeammates_AndSendsTheWhisper()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/asks"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var cut = Render(MeJson(asks: true));

        cut.FindAll(".ask-target").Select(l => l.TextContent.Trim()).Should().Equal("Merlin", "Lyra");
        cut.Find("#ask-send").HasAttribute("disabled").Should().BeTrue("no recipient and no text yet");
        cut.FindAll(".ask-target input").First().Change(true);
        cut.Find("#ask-kind").Change("accusation");
        cut.Find("#ask-text").Input("Did you take the gold?");
        cut.Find("#ask-send").Click();

        var post = _handler.SocialRequests.Single(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(post.Body!).RootElement;
        body.GetProperty("to").EnumerateArray().Select(x => x.GetString()).Should().Equal("Merlin");
        body.GetProperty("kind").GetString().Should().Be("accusation");
        body.GetProperty("text").GetString().Should().Be("Did you take the gold?");
    }

    [Fact]
    public void AnInboxWhisper_CanBeAnsweredHonestlyDodgedOrBluffed()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/asks/ask_7/reply"] = (HttpStatusCode.OK, "{\"accepted\":true}");
        var inbox = "[{\"askId\":\"ask_7\",\"from\":\"Merlin\",\"kind\":\"question\",\"text\":\"Who has the key?\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"replied\":false}]";
        var cut = Render(MeJson(asks: true, inbox: inbox));

        cut.Find(".inbox-ask").TextContent.Should().Contain("Merlin").And.Contain("Who has the key?");
        cut.Find(".reply-text").Input("Lyra has it");
        cut.Find(".reply-bluff").Click();

        var post = _handler.SocialRequests.Single(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(post.Body!).RootElement;
        body.GetProperty("choice").GetString().Should().Be("bluff");
        body.GetProperty("text").GetString().Should().Be("Lyra has it");
    }

    [Fact]
    public void SentWhispers_ShowTheRepliesAsTheyReadToTheAsker()
    {
        var sent = "[{\"askId\":\"ask_1\",\"to\":[\"Merlin\"],\"kind\":\"question\",\"text\":\"Who has the key?\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"replies\":[{\"by\":\"Merlin\",\"summary\":\"slips the question\"}]}]";

        var cut = Render(MeJson(asks: true, sent: sent));

        cut.Find("#sent").TextContent.Should().Contain("Merlin").And.Contain("slips the question");
    }

    [Fact]
    public void AWhisperArriving_RefreshesThePanel()
    {
        var cut = Render(MeJson(asks: true));
        cut.FindAll(".inbox-ask").Should().BeEmpty();
        var inbox = "[{\"askId\":\"ask_9\",\"from\":\"Lyra\",\"kind\":\"offer\",\"text\":\"Share the loot?\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"replied\":false}]";
        _handler.SocialRoutes[$"GET /api/parties/{Id}/social/me"] = (HttpStatusCode.OK, MeJson(asks: true, inbox: inbox));

        _hub.RaisePrivate(new PrivateMessageDto("ask", "Lyra whispers: Share the loot?"));

        cut.WaitForAssertion(() => cut.Find(".inbox-ask").TextContent.Should().Contain("Share the loot?"));
    }

    [Fact]
    public void ARejectedCall_ShowsTheServersMessage()
    {
        _handler.SocialRoutes[$"POST /api/parties/{Id}/social/asks"] = (HttpStatusCode.TooManyRequests, "{\"detail\":\"Too soon.\",\"code\":\"social.ask_cooldown\"}");
        var cut = Render(MeJson(asks: true));
        cut.FindAll(".ask-target input").First().Change(true);
        cut.Find("#ask-text").Input("Hello");

        cut.Find("#ask-send").Click();

        cut.WaitForAssertion(() => cut.Find("#asks-error").TextContent.Should().NotBeNullOrWhiteSpace());
    }
}
