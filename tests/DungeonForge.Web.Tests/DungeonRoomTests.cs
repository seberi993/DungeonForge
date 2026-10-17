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
        handler.SeparateSpells = true;
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
    public void Resolve_Success_UpdatesStatsAndAppendsLogEntry()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "chest", null, 0, 15, "Shiny chest", 15)));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find("#stat-gold").TextContent.Should().Contain("15");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        var entry = cut.Find(".log-entry").TextContent;
        entry.Should().Contain("Shiny chest").And.Contain("chest").And.Contain("15");
    }

    [Fact]
    public void Resolve_Combat_ReducesTargetMemberHpAndLogsDamage()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 4, 0, "Clash", 0)));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#member-0").TextContent.Should().Contain("26/30"));
        cut.Find("#member-1").TextContent.Should().Contain("16/16");
        cut.Find("#stat-hp").TextContent.Should().Contain("42/46");
        cut.Find(".log-entry").TextContent.Should().Contain("Conan took 4 damage");
    }

    [Fact]
    public void SoundToggle_FlipsMuteThroughTheSfxModule_AndShowsTheState()
    {
        JSInterop.Setup<bool>("dungeonForge.sfx.isMuted").SetResult(false);
        JSInterop.Setup<bool>("dungeonForge.sfx.toggle").SetResult(true);
        var cut = Render(new FakeHandler(HttpStatusCode.OK));

        cut.Find("#toggle-sound").TextContent.Should().Contain("Sound on");
        cut.Find("#toggle-sound").Click();

        cut.WaitForAssertion(() => cut.Find("#toggle-sound").TextContent.Should().Contain("Sound off"));
    }

    [Fact]
    public async Task StageCues_ArePlayedThroughTheSfxModule()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));

        await cut.InvokeAsync(() => cut.FindComponent<AsciiStage>().Instance.OnCue.InvokeAsync("crit"));

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "dungeonForge.sfx.play" && (string)i.Arguments[0]! == "crit");
    }

    [Fact]
    public void MemberList_ShowsAnHpBarPerMember_FullAtStart()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));

        var bars = cut.FindAll("#members .hp-fill");
        bars.Should().HaveCount(2);
        bars.Should().OnlyContain(b => b.GetAttribute("style")!.Contains("width:100%") && !b.ClassList.Contains("hp-low"));
        cut.Find("#member-0 .hp-bar").GetAttribute("aria-valuenow").Should().Be("30");
        cut.Find("#member-0 .hp-bar").GetAttribute("aria-valuemax").Should().Be("30");
    }

    [Fact]
    public void Resolve_ShrinksTheHitMembersBar_AndTurnsItRedWhenLow()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ExploreJson(1, "combat", "Conan", 22, 0, "Clash", 0)));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#member-0 .hp-fill").GetAttribute("style").Should().Contain("width:27%"));
        cut.Find("#member-0 .hp-fill").ClassList.Should().Contain("hp-low");
        cut.Find("#member-1 .hp-fill").ClassList.Should().NotContain("hp-low");
    }

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

    private static string PromptJson(string discussion = "[]") =>
        $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"A troll blocks the bridge.\nIt growls.","gold":0,"isPartyDefeated":false,"scenario":"A troll blocks the bridge.\nIt growls.","scenarioPending":false,"discussion":{{discussion}}}""";

    private static string PendingResolveJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"puzzle","damage":0,"goldFound":20,"flavorEvent":"Solved","gold":20,"isPartyDefeated":false,"narrationPending":true}""";

    private static string ResolveWithOutcomeJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"combat","targetName":"Conan","damage":3,"goldFound":0,"flavorEvent":"Resolved","gold":0,"isPartyDefeated":false,"lastOutcome":"You bribed the troll and slipped past."}""";

    private static readonly string Scenario0 = "A troll blocks the bridge.\nIt growls.";

    private IRenderedComponent<DungeonRoom> RenderWithScenario(FakeHandler handler, TimeSpan? narrationTimeout = null)
    {
        handler.SeparateSpells = true;
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        _session.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, Scenario0, 0, false, Scenario: Scenario0));
        Services.AddSingleton<IDungeonHubClient>(_hub);
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>(p =>
        {
            if (narrationTimeout is { } t) p.Add(c => c.NarrationTimeout, t);
        });
    }

    private static void Resolve(IRenderedComponent<DungeonRoom> cut, string text = "Attack the troll")
    {
        cut.Find("#action-text").Input(text);
        cut.Find("button#lock-in").Click();
    }

    [Fact]
    public void Explore_ShowsScenarioWithLineBreaks_AndNoOptionButtons()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, PromptJson());
        var cut = Render(handler);

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("#scenario"));
        handler.LastRequestBody.Should().BeNull();
        cut.Find("#scenario").TextContent.Should().Be(Scenario0);
        cut.Find("#scenario").ClassList.Should().Contain("scenario");
        cut.FindAll("[id^=choice]").Should().BeEmpty();
        cut.FindAll("#explore").Should().BeEmpty();
        cut.FindAll(".log-entry").Should().BeEmpty();
        cut.Find("#stat-turn").TextContent.Should().Contain("0");
    }

    [Fact]
    public void ScenarioPending_PromptResponse_ShowsDungeonShiftsPlaceholder()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"x","gold":0,"isPartyDefeated":false,"scenarioPending":true}"""));

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => cut.Find("#scenario-pending").TextContent.Should().Contain("dungeon shifts"));
        cut.FindAll("#scenario").Should().BeEmpty();
        cut.FindAll("#lock-in").Should().BeEmpty();
    }

    [Fact]
    public void ActionBox_HasMaxLength200_AndResolveDisabledUntilAnActionExists()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.Find("#action-text").GetAttribute("maxlength").Should().Be("200");
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();

        cut.Find("#action-text").Input("   ");
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();

        cut.Find("#action-text").Input("Shield the mage");
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TeammateSubmittedAction_IsNeverShown_AndLockInStillNeedsOwnText()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseDiscussion(Id, [new PlayerAction(Guid.NewGuid(), "Merlin", "Light a torch")]));

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Light a torch"));
        cut.FindAll("#discussion").Should().BeEmpty();
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void SubmitAction_PostsSubmitOnlyBody_HidesOthersAnswers_AndKeepsScenario()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            PromptJson($$"""[{"memberId":"{{_session.PlayerId}}","name":"Conan","text":"Press the left rune"},{"memberId":"{{Guid.NewGuid()}}","name":"Merlin","text":"Read the glyphs"}]"""));
        var cut = RenderWithScenario(handler);

        cut.Find("#action-text").Input("Press the left rune");
        cut.Find("button#lock-in").Click();

        cut.WaitForAssertion(() => cut.Find("button#lock-in").TextContent.Should().Contain("Locked in"));
        handler.LastRequestBody.Should().Be($"{{\"actions\":[{{\"memberId\":\"{_session.PlayerId}\",\"name\":\"Conan\",\"text\":\"Press the left rune\"}}],\"submitOnly\":true}}");
        cut.Markup.Should().NotContain("Read the glyphs");
        cut.Find("#scenario").Should().NotBeNull();
        cut.FindAll(".log-entry").Should().BeEmpty();
    }

    [Fact]
    public void SubmitAction_BlankText_DoesNothing()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, PromptJson());
        var cut = RenderWithScenario(handler);
        cut.Find("#action-text").Input("   ");
        cut.Find("button#lock-in").Click();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public void LockIn_SendsOnlyOwnAction_WithSubmitOnly()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, PendingResolveJson());
        var cut = RenderWithScenario(handler);

        Resolve(cut, "Charge");

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        handler.LastRequestBody.Should().Contain("\"text\":\"Charge\"").And.Contain("\"submitOnly\":true");
        handler.LastRequestBody.Should().NotContain("Light a torch");
    }

    [Fact]
    public void Resolve_WithOutcome_AppliesMechanicsAndShowsOutcome()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, ResolveWithOutcomeJson()));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.Find(".log-entry").TextContent.Should().Contain("You bribed the troll and slipped past.");
        cut.Find("#last-outcome").TextContent.Should().Contain("You bribed the troll");
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#member-0").TextContent.Should().Contain("27/30");
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NarrationPending_AppliesMechanicsImmediately_WithPlaceholders_AndNoExplore()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#stat-gold").TextContent.Should().Contain("20"));
        cut.Find("#stat-turn").TextContent.Should().Contain("1");
        cut.Find("#narration-loading").Should().NotBeNull();
        cut.Find("#scenario-pending").TextContent.Should().Contain("dungeon shifts");
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
        cut.FindAll("#explore").Should().BeEmpty();
    }

    [Fact]
    public void NarrationReady_MatchingTurn_ShowsOutcomeAndNextScenario()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "The runes hum and the door opens.", "A vast hall\nstretches ahead.", RoomType.Chest)));

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().Contain("door opens"));
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.Find(".log-outcome").TextContent.Should().Contain("door opens");
        cut.Find("#scenario").TextContent.Should().Be("A vast hall\nstretches ahead.");
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void NarrationReady_WrongTurn_IsIgnored()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 5, "wrong", "wrong scenario")));

        cut.Find("#narration-loading").Should().NotBeNull();
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void NarrationReady_PartyDefeated_NoNextScenario_ShowsOutcomeOnly()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.Find("#narration-loading"));

        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "Darkness takes you.")));

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().Contain("Darkness"));
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.FindAll("#scenario").Should().BeEmpty();
    }

    [Fact]
    public void NarrationNeverArrives_FallsBackToPlaceholderText_AndOffersExploreAgain()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()), TimeSpan.FromMilliseconds(50));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.Find("#last-outcome").TextContent.Should().NotBeNullOrWhiteSpace(), TimeSpan.FromSeconds(3));
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#scenario-pending").Should().BeEmpty();
        cut.Find("button#explore").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Resolve_Twice_AppendsEntries_ViaNextScenario()
    {
        var turn = 0;
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, bodyFactory: () => ExploreJson(++turn, "combat", "Conan", 1, 0, "Step", 0)));
        Resolve(cut);
        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "o", "Next room.")));
        cut.WaitForAssertion(() => cut.Find("#scenario").TextContent.Should().Be("Next room."));

        Resolve(cut, "Again");

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(2));
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
    public void RemoteResolvedTurn_ClearsScenario_AndShowsPlaceholder()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Chest, null, 0, 7, "Remote", 7)));

        cut.WaitForAssertion(() => cut.FindAll("#scenario").Should().BeEmpty());
        cut.Find("#scenario-pending").Should().NotBeNull();
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

    [Fact]
    public void PrivateMessage_ShowsInWhisperPanel()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaisePrivate(new PrivateMessageDto("whisper", "Merlin pocketed 12 gold.")));

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Light a torch"));
        cut.FindAll("#discussion").Should().BeEmpty();
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

    private static string LockedPromptJson() =>
        $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"x","gold":0,"isPartyDefeated":false,"scenario":"A troll blocks the bridge.\nIt growls.","scenarioPending":false,"lockStatus":[{"name":"Conan","lockedIn":true},{"name":"Merlin","lockedIn":false}]}""";

    [Fact]
    public void LockIn_DisablesButtonAndInput_ShowsUnlock_AndWaitingStatus()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, LockedPromptJson()));

        cut.Find("#action-text").Input("Charge");
        cut.Find("button#lock-in").Click();

        cut.WaitForAssertion(() => cut.Find("button#unlock-turn"));
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#action-text").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".party-lock-status .lock-summary").TextContent.Should().Contain("(1/2)");
        cut.FindAll(".log-entry").Should().BeEmpty();
    }

    [Fact]
    public void Mana_StartsFull_SpendsOnSpellLockIn_AndRefundsOnUnlock()
    {
        var calls = 0;
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            calls++ == 0
                ? LockedPromptJson()
                : $$"""{"partyId":"{{Id}}","lockStatus":[{"name":"Conan","lockedIn":false},{"name":"Merlin","lockedIn":false}]}""")
        {
            SpellsBody = """[{"name":"Smite","classId":"warrior","element":"holy","manaCost":5,"description":"Holy blow."}]"""
        };
        var cut = RenderWithScenario(handler);
        var max = ManaRules.MaxFor(CharacterClass.Warrior);
        cut.Find("#member-0 .mana-bar").GetAttribute("aria-valuenow").Should().Be(max.ToString());
        cut.WaitForState(() => _session.HasSpells);
        cut.Find("#action-text").Input("I cast Smite on the troll");

        cut.Find("button#lock-in").Click();

        cut.WaitForAssertion(() => cut.Find("#member-0 .mana-bar").GetAttribute("aria-valuenow").Should().Be((max - 5).ToString()));
        cut.Find("button#unlock-turn").Click();
        cut.WaitForAssertion(() => cut.Find("#member-0 .mana-bar").GetAttribute("aria-valuenow").Should().Be(max.ToString()));
    }

    [Fact]
    public void Unlock_PostsMemberId_ReenablesLockIn_AndUpdatesStatus()
    {
        var calls = 0;
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            calls++ == 0
                ? LockedPromptJson()
                : $$"""{"partyId":"{{Id}}","lockStatus":[{"name":"Conan","lockedIn":false},{"name":"Merlin","lockedIn":false}]}""");
        var cut = RenderWithScenario(handler);
        cut.Find("#action-text").Input("Charge");
        cut.Find("button#lock-in").Click();
        cut.WaitForAssertion(() => cut.Find("button#unlock-turn"));

        cut.Find("button#unlock-turn").Click();

        cut.WaitForAssertion(() => cut.FindAll("button#unlock-turn").Should().BeEmpty());
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}/unlock");
        handler.LastRequestBody.Should().Be($"{{\"memberId\":\"{_session.PlayerId}\"}}");
        cut.Find("#action-text").HasAttribute("disabled").Should().BeFalse();
        cut.Find("button#lock-in").HasAttribute("disabled").Should().BeFalse();
        cut.Find(".lock-summary").TextContent.Should().Contain("(0/2)");
    }

    [Fact]
    public void TurnLockUpdated_FromHub_UpdatesWaitingIndicator()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseLock(Id, [new LockStatusDto("Conan", false), new LockStatusDto("Merlin", true)]));

        cut.WaitForAssertion(() => cut.Find(".lock-summary").TextContent.Should().Contain("(1/2)"));
    }

    [Fact]
    public void LastLockIn_ResolvesTurn_ClearsLockedState()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));

        Resolve(cut);

        cut.WaitForAssertion(() => cut.FindAll(".log-entry").Should().HaveCount(1));
        cut.FindAll("button#unlock-turn").Should().BeEmpty();
        cut.FindAll(".party-lock-status").Should().BeEmpty();
    }

    [Fact]
    public void Waiting409_KeepsLockedState_WithoutDefeat()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.Conflict,
            $$"""{"partyId":"{{Id}}","locked":1,"required":2,"lockStatus":[],"status":"waiting"}"""));

        cut.Find("#action-text").Input("Charge");
        cut.Find("button#lock-in").Click();

        cut.WaitForAssertion(() => cut.Find("button#unlock-turn"));
        _session.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void SpellbookToggle_OpensDrawer_SpellsAreReadOnlyAndLeaveActionBoxUntouched()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """[{"name":"Smite","classId":"warrior","element":"holy","manaCost":5,"description":"Holy blow."}]""");
        var cut = RenderWithScenario(handler);
        cut.FindAll(".spell").Should().BeEmpty();

        cut.Find("button#toggle-spellbook").Click();
        cut.WaitForAssertion(() => cut.FindAll(".spell").Should().HaveCount(1));
        handler.LastSpellsRequest!.RequestUri!.AbsolutePath.Should().Be("/api/classes/warrior/spells");
        cut.Find("#action-text").Input("Charge");

        cut.FindAll(".spell button").Should().BeEmpty();
        cut.Find("#action-text").GetAttribute("value").Should().Be("Charge");
    }

    [Fact]
    public void FocusedView_ShowsSceneArt_AuditClosedByDefault_AndMarksMyHero()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK, PendingResolveJson()));

        cut.Find("#ascii-stage .ascii-scene").Should().NotBeNull();
        cut.Find("#scenario-panel").Should().NotBeNull();
        cut.Find("details#audit").HasAttribute("open").Should().BeFalse();
        cut.Find("details#audit summary").TextContent.Should().Contain("0 rooms");
        cut.Find("#member-0").ClassList.Should().Contain("me");
    }

    [Fact]
    public void TurnWithBeats_ActsItOutOnTheStage_AndHoldsTheTextUntilDone()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));
        ExploreResponse resolved = new(Id, 1, RoomType.Combat, "Conan", 3, 0, "Clash", 0, false, NarrationPending: true,
            Beats: [new ActionBeat("Conan", CharacterClass.Warrior, ActionVerb.Strike, "I charge the troll"), new ActionBeat("Merlin", CharacterClass.Mage, ActionVerb.Cast, "A fireball")],
            Success: true);

        cut.InvokeAsync(() => _hub.RaiseTurn(resolved));
        cut.InvokeAsync(() => _hub.RaiseNarration(new NarrationReady(Id, 1, "The troll falls.", "A **door** looms.", RoomType.Puzzle, "door")));

        cut.WaitForAssertion(() => cut.Find(".stage-caption").TextContent.Should().Contain("Conan").And.Contain("I charge the troll"));
        cut.FindAll("#last-outcome").Should().BeEmpty();
        cut.FindAll("#scenario-panel").Should().BeEmpty();
        cut.FindAll("#narration-loading").Should().BeEmpty();
        cut.FindAll("#ascii-stage").Should().HaveCount(1);
    }

    [Fact]
    public void TurnWithoutBeats_KeepsTheClassicFlow()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaiseTurn(Turn(1, RoomType.Combat, "Conan", 3, 0, "Clash", 0) with { NarrationPending = true }));

        cut.WaitForAssertion(() => cut.Find("#narration-loading"));
    }

    [Fact]
    public async Task Dispose_DisposesHubClient()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK));
        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync().AsTask());
        _hub.Disposed.Should().BeTrue();
    }
}
