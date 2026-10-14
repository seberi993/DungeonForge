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

    private static string PromptJson() =>
        $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"A troll blocks the bridge.","gold":0,"isPartyDefeated":false,"choicePrompt":"A troll blocks the bridge.","choices":["Fight","Sneak","Bribe"]}""";

    private static string ResolveJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"combat","targetName":"Conan","damage":3,"goldFound":0,"flavorEvent":"Resolved","gold":0,"isPartyDefeated":false,"lastOutcome":"You bribed the troll and slipped past."}""";

    // First POST (no body) returns the prompt; POSTs carrying a body resolve.
    private static FakeHandler PromptThenResolve()
    {
        FakeHandler? h = null;
        h = new FakeHandler(HttpStatusCode.OK, bodyFactory: () => h!.LastRequestBody is null ? PromptJson() : ResolveJson());
        return h;
    }

    private IRenderedComponent<DungeonRoom> RenderWithPending(FakeHandler handler)
    {
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        _session.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, "p", 0, false, "p", ["a", "b", "c"]));
        return Render(handler, withParty: false);
    }

    [Fact]
    public void Prompt_RendersThreeChoiceButtons_AndHidesExplore()
    {
        var cut = Render(PromptThenResolve());

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("#choice-0"));
        cut.Find("#choice-0").TextContent.Should().Be("Fight");
        cut.Find("#choice-1").TextContent.Should().Be("Sneak");
        cut.Find("#choice-2").TextContent.Should().Be("Bribe");
        cut.FindAll("#choice-3").Should().BeEmpty();
        cut.Find("#choice-prompt").TextContent.Should().Contain("troll");
        cut.FindAll("#explore").Should().BeEmpty();
        cut.FindAll(".log-entry").Should().BeEmpty();
        cut.Find("#stat-turn").TextContent.Should().Contain("0");
    }

    [Fact]
    public void Explore_SendsNoBody()
    {
        var handler = PromptThenResolve();
        var cut = Render(handler);
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.Find("#choice-0"));
        handler.LastRequestBody.Should().BeNull();
    }

    [Fact]
    public void ClickingChoice_SendsIndexInBody_ShowsLastOutcome_AndRestoresExplore()
    {
        var handler = PromptThenResolve();
        var cut = Render(handler);
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.Find("#choice-2"));

        cut.Find("#choice-2").Click();

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        handler.LastRequestBody.Should().Be("{\"selectedChoiceIndex\":2}");
        cut.Find(".log-entry").TextContent.Should().Contain("You bribed the troll and slipped past.");
        cut.Find("#last-outcome").TextContent.Should().Contain("You bribed the troll");
        cut.FindAll("#choice-0").Should().BeEmpty();
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#member-0").TextContent.Should().Contain("27/30");
    }

    [Fact]
    public void ChoiceRejectedWith400_ShowsErrorAndRestoresExplore()
    {
        var cut = RenderWithPending(new FakeHandler(HttpStatusCode.BadRequest,
            """{"errors":{"selectedChoiceIndex":["No active encounter."]}}"""));

        cut.Find("#choice-1").Click();

        cut.WaitForAssertion(() => cut.Find(".error").TextContent.Should().Contain("No active encounter."));
        cut.FindAll("#choice-0").Should().BeEmpty();
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void RemoteResolvedTurn_ClearsPendingChoices()
    {
        var cut = RenderWithPending(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Remote", 7)));

        cut.WaitForAssertion(() => cut.FindAll("#choice-0").Should().BeEmpty());
        cut.Find("button#explore").Should().NotBeNull();
    }

    private static string PuzzlePromptJson(string discussion = "[]") =>
        $$"""{"partyId":"{{Id}}","turn":0,"roomType":"puzzle","damage":0,"goldFound":0,"flavorEvent":"Three runes glow.","gold":0,"isPartyDefeated":false,"choicePrompt":"Three runes glow.","choices":["Left","Middle","Right"],"discussion":{{discussion}}}""";

    private static string PendingResolveJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"puzzle","damage":0,"goldFound":20,"flavorEvent":"Solved","gold":20,"isPartyDefeated":false,"narrationPending":true}""";

    private IRenderedComponent<DungeonRoom> RenderPuzzle(FakeHandler handler, TimeSpan? narrationTimeout = null)
    {
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        _session.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Puzzle, null, 0, 0, "Three runes glow.", 0, false, "Three runes glow.", ["Left", "Middle", "Right"]));
        Services.AddSingleton<IDungeonHubClient>(_hub);
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>(p =>
        {
            if (narrationTimeout is { } t) p.Add(c => c.NarrationTimeout, t);
        });
    }

    [Fact]
    public void PuzzlePrompt_ShowsPromptChoicesAndActionInput()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK));
        cut.Find("#choice-prompt").TextContent.Should().Contain("runes");
        cut.Find("#choice-2").TextContent.Should().Be("Right");
        cut.Find("#action-text").Should().NotBeNull();
        cut.Find("button#submit-action").TextContent.Should().Contain("Submit action");
    }

    [Fact]
    public void ExplorePuzzle_PromptResponse_ShowsActionInput()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, PuzzlePromptJson()));
        cut.Find("button#explore").Click();
        cut.WaitForAssertion(() => cut.Find("#choice-0"));
        cut.Find("#action-text").Should().NotBeNull();
        cut.FindAll(".log-entry").Should().BeEmpty();
    }

    [Fact]
    public void SubmitAction_PostsActionOnlyBody_AndListsDiscussion()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            PuzzlePromptJson($$"""[{"memberId":"{{_session.PlayerId}}","name":"Conan","text":"Press the left rune"},{"memberId":"{{Guid.NewGuid()}}","name":"Merlin","text":"Read the glyphs"}]"""));
        var cut = RenderPuzzle(handler);

        cut.Find("#action-text").Input("Press the left rune");
        cut.Find("button#submit-action").Click();

        cut.WaitForAssertion(() => cut.FindAll("#discussion li").Should().HaveCount(2));
        handler.LastRequestBody.Should().Be($"{{\"actions\":[{{\"memberId\":\"{_session.PlayerId}\",\"name\":\"Conan\",\"text\":\"Press the left rune\"}}]}}");
        cut.Find("#discussion").TextContent.Should().Contain("Merlin").And.Contain("Read the glyphs");
        cut.FindAll("#choice-0").Should().HaveCount(1);
    }

    [Fact]
    public void SubmitAction_BlankText_DoesNothing()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, PuzzlePromptJson());
        var cut = RenderPuzzle(handler);
        cut.Find("#action-text").Input("   ");
        cut.Find("button#submit-action").Click();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public void RemoteDiscussionUpdate_ReplacesTeammateList()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK));
        var actions = new[] { new PlayerAction(Guid.NewGuid(), "Merlin", "Light a torch") };

        cut.InvokeAsync(() => _hub.RaiseDiscussion(Id, actions));

        cut.WaitForAssertion(() => cut.Find("#discussion").TextContent.Should().Contain("Merlin").And.Contain("Light a torch"));
    }

    [Fact]
    public void Resolve_NarrationPending_AppliesMechanicsImmediately_WithLoadingPlaceholder_AndInputStillUsable()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));

        cut.Find("#choice-1").Click();

        cut.WaitForAssertion(() => cut.Find("#stat-gold").TextContent.Should().Contain("20"));
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#narration-loading").Should().NotBeNull();
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void NarrationReady_MatchingTurn_ReplacesPlaceholder()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        cut.Find("#choice-1").Click();
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "The runes hum and the door opens.")));

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().Contain("door opens"));
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.Find(".log-outcome").TextContent.Should().Contain("door opens");
    }

    [Fact]
    public void NarrationReady_WrongTurn_IsIgnored()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        cut.Find("#choice-1").Click();
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 5, "wrong")));

        cut.Find("#narration-loading").Should().NotBeNull();
        cut.FindAll("#last-outcome").Should().BeEmpty();
    }

    [Fact]
    public void NarrationNeverArrives_FallsBackToPlaceholderText()
    {
        var cut = RenderPuzzle(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()), TimeSpan.FromMilliseconds(50));

        cut.Find("#choice-0").Click();

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().NotBeNullOrWhiteSpace(), TimeSpan.FromSeconds(3));
        cut.FindAll("#narration-loading").Should().BeEmpty();
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
