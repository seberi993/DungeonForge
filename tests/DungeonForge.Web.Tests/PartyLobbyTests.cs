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
}
