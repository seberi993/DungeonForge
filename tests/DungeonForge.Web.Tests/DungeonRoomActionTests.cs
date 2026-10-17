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

public class DungeonRoomActionTests : DungeonRoomTestBase
{
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
    public void PrivateMessage_ShowsInWhisperPanel()
    {
        var cut = RenderWithScenario(new FakeHandler(HttpStatusCode.OK));

        cut.InvokeAsync(() => _hub.RaisePrivate(new PrivateMessageDto("whisper", "Merlin pocketed 12 gold.")));

        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Light a torch"));
        cut.FindAll("#discussion").Should().BeEmpty();
    }
}
