using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Web.Core;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Lobby;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class ScenePainterTests : DungeonRoomTestBase
{
    private static string SetupJson(bool answered = false, int count = 0, bool pending = true) =>
        $$"""{"partyId":"{{Id}}","question":"Where are you?","answered":{{answered.ToString().ToLowerInvariant()}},"answeredCount":{{count}},"total":2,"pending":{{pending.ToString().ToLowerInvariant()}}}""";

    private IRenderedComponent<ScenePainter> RenderPainter(FakeHandler handler)
    {
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM", SetupPending: true));
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<ScenePainter>();
    }

    [Fact]
    public void ShowsTheDmsQuestion_AndTheSubmitButtonWaitsForText()
    {
        var cut = RenderPainter(new FakeHandler(HttpStatusCode.OK, SetupJson()));

        cut.WaitForAssertion(() => cut.Find("#scene-question").TextContent.Should().Be("Where are you?"));
        cut.Find("#scene-submit").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#scene-answer").Input("A drowned chapel");
        cut.Find("#scene-submit").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Submitting_PostsTheAnswer_AndThenWaitsForTheOthers()
    {
        var calls = 0;
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            calls++ == 0 ? SetupJson() : $$"""{"partyId":"{{Id}}","answeredCount":1,"total":2,"pending":true}""");
        var cut = RenderPainter(handler);
        cut.WaitForElement("#scene-answer").Input("A drowned chapel");

        cut.Find("#scene-submit").Click();

        cut.WaitForAssertion(() => cut.Find("#scene-waiting").TextContent.Should().Contain("1 of 2"));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}/setup");
        handler.LastRequestBody.Should().Contain("\"playerName\":\"Conan\"").And.Contain("A drowned chapel");
        _session.SetupPending.Should().BeTrue();
    }

    [Fact]
    public void LastAnswer_ReleasesTheGate()
    {
        var calls = 0;
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () =>
            calls++ == 0 ? SetupJson(count: 1) : $$"""{"partyId":"{{Id}}","answeredCount":2,"total":2,"pending":false}""");
        var cut = RenderPainter(handler);
        cut.WaitForElement("#scene-answer").Input("A burning inn");

        cut.Find("#scene-submit").Click();

        cut.WaitForAssertion(() => _session.SetupPending.Should().BeFalse());
    }

    [Fact]
    public void Room_ShowsThePainter_InsteadOfTheStage_WhileSetupIsPending()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.OK, SetupJson()), withParty: false);
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false)], DateTimeOffset.UtcNow, "K7F2-9QXM", SetupPending: true));
        cut.Render();

        cut.FindAll("#scene-painter").Should().HaveCount(1);
        cut.FindAll("#ascii-stage, #explore").Should().BeEmpty();
    }
}
