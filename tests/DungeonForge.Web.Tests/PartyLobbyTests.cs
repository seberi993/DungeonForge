using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class PartyLobbyTests : TestContext
{
    private readonly GameSession _session = new();
    private FakeNavigationManager Nav => Services.GetRequiredService<FakeNavigationManager>();
    private static readonly Guid Id = Guid.NewGuid();

    private IRenderedComponent<PartyLobby> Render(FakeHandler handler)
    {
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<PartyLobby>();
    }

    [Fact]
    public void Renders_NameInputAndCreateButton()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Created));
        cut.Find("input#party-name").Should().NotBeNull();
        cut.Find("button#create-party").TextContent.Should().Contain("Create");
    }

    [Fact]
    public void Create_Success_StoresSessionAndNavigatesToDungeon()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Created,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":["A"],"createdAt":"2026-01-01T00:00:00+00:00"}"""));

        cut.Find("input#party-name").Change("Heroes");
        cut.Find("button#create-party").Click();

        cut.WaitForAssertion(() => Nav.Uri.Should().EndWith("/dungeon"));
        _session.HasParty.Should().BeTrue();
        _session.PartyId.Should().Be(Id);
    }

    [Fact]
    public void Create_ValidationError_ShowsMessageAndDoesNotNavigate()
    {

        var cut = Render(new FakeHandler(HttpStatusCode.BadRequest,
            """{"errors":{"name":["Name is required."]}}"""));

        cut.Find("button#create-party").Click();

        cut.WaitForAssertion(() => cut.Find(".validation-message").TextContent.Should().Contain("Name is required."));
        new Uri(Nav.Uri).AbsolutePath.Should().Be("/");
        _session.HasParty.Should().BeFalse();
    }

    [Fact]
    public void Create_ServerFailure_ShowsErrorMessage()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.InternalServerError));
        cut.Find("input#party-name").Change("Heroes");
        cut.Find("button#create-party").Click();
        cut.WaitForAssertion(() => cut.Find(".validation-message").TextContent.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Join_Success_LoadsStateAndNavigatesToDungeon()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":["A"],"createdAt":"2026-01-01T00:00:00+00:00","hp":9,"gold":12,"turn":3}""");
        var cut = Render(handler);

        cut.Find("input#join-id").Change(Id.ToString());
        cut.Find("button#join-party").Click();

        cut.WaitForAssertion(() => Nav.Uri.Should().EndWith("/dungeon"));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}");
        _session.PartyId.Should().Be(Id);
        _session.Hp.Should().Be(9);
        _session.Turn.Should().Be(3);
    }

    [Fact]
    public void Join_ByInviteCode_UsesCodeEndpointAndStoresCode()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":[],"createdAt":"2026-01-01T00:00:00+00:00","hp":9,"gold":12,"turn":3,"inviteCode":"K7F2-9QXM"}""");
        var cut = Render(handler);

        cut.Find("input#join-id").Change(" k7f2-9qxm ");
        cut.Find("button#join-party").Click();

        cut.WaitForAssertion(() => Nav.Uri.Should().EndWith("/dungeon"));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/parties/by-code/k7f2-9qxm");
        _session.PartyId.Should().Be(Id);
        _session.InviteCode.Should().Be("K7F2-9QXM");
    }

    [Fact]
    public void Join_EmptyInput_ShowsErrorWithoutRequest()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        var cut = Render(handler);

        cut.Find("input#join-id").Change("   ");
        cut.Find("button#join-party").Click();

        cut.Find(".join-error").TextContent.Should().NotBeNullOrWhiteSpace();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public void Join_NotFound_ShowsError()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.NotFound));

        cut.Find("input#join-id").Change(Id.ToString());
        cut.Find("button#join-party").Click();

        cut.WaitForAssertion(() => cut.Find(".join-error").TextContent.Should().Contain("No party"));
        _session.HasParty.Should().BeFalse();
    }
}
