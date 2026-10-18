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

public class DungeonRoomLockTests : DungeonRoomTestBase
{
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
}
