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

public class DungeonRoomStatsTests : DungeonRoomTestBase
{
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
    public void Room_HidesSpellbookClassesAndAbilities()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.FindAll("#toggle-spellbook, .spellbook-drawer, .member-class, .abilities").Should().BeEmpty();
    }

    [Fact]
    public void DmFeed_SpeaksInOrder_Challenge_ThenWhispers_ThenTheActionBox()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));
        cut.InvokeAsync(() => _hub.RaisePrivate(new PrivateMessageDto("whisper", "Merlin pocketed 12 gold.")));

        cut.WaitForAssertion(() =>
        {
            var html = cut.Find("#scenario-panel").InnerHtml;
            var challenge = html.IndexOf("The challenge", StringComparison.Ordinal);
            var whispers = html.IndexOf("id=\"whispers\"", StringComparison.Ordinal);
            var action = html.IndexOf("id=\"action-box\"", StringComparison.Ordinal);
            challenge.Should().BeGreaterThanOrEqualTo(0);
            challenge.Should().BeLessThan(whispers);
            whispers.Should().BeLessThan(action);
        });
        cut.FindAll("#whispers").Should().HaveCount(1);
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
}
