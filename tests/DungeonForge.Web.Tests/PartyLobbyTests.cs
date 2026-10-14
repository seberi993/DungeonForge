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
    public void Create_PostsRosterRowsWithChosenClasses()
    {
        var handler = new FakeHandler(HttpStatusCode.Created,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":[],"createdAt":"2026-01-01T00:00:00+00:00"}""");
        var cut = Render(handler);

        cut.Find("input#party-name").Change("Heroes");
        cut.Find("input#member-name-0").Change("Conan");
        cut.Find("button#add-member").Click();
        cut.Find("input#member-name-1").Change("Merlin");
        cut.Find("select#member-class-1").Change("Mage");
        cut.Find("button#create-party").Click();

        cut.WaitForAssertion(() => Nav.Uri.Should().EndWith("/dungeon"));
        handler.LastRequestBody.Should().Contain("\"members\":[{\"name\":\"Conan\",\"class\":\"warrior\"},{\"name\":\"Merlin\",\"class\":\"mage\"}]");
    }

    [Fact]
    public void RosterButtons_AddAndRemoveRows()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Created));
        cut.FindAll(".roster-row").Should().HaveCount(1);
        cut.Find("button#remove-member").HasAttribute("disabled").Should().BeTrue();

        cut.Find("button#add-member").Click();
        cut.FindAll(".roster-row").Should().HaveCount(2);

        cut.Find("button#remove-member").Click();
        cut.FindAll(".roster-row").Should().HaveCount(1);
    }

    [Fact]
    public void Create_Success_StoresSessionAndNavigatesToDungeon()
    {
        var cut = Render(new FakeHandler(HttpStatusCode.Created,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":[{"name":"A","class":"warrior","hp":30,"maxHp":30,"isDefeated":false}],"createdAt":"2026-01-01T00:00:00+00:00"}"""));

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

    private static string StateJson(string members) =>
        $$"""{"partyId":"{{Id}}","name":"Heroes","members":[{{members}}],"createdAt":"2026-01-01T00:00:00+00:00","gold":12,"turn":3,"isPartyDefeated":false,"inviteCode":"K7F2-9QXM"}""";

    private const string Conan = """{"name":"Conan","class":"warrior","hp":9,"maxHp":30,"isDefeated":false}""";
    private const string Zed = """{"name":"Zed","class":"mage","hp":16,"maxHp":16,"isDefeated":false}""";

    [Fact]
    public void Join_Find_ShowsHeroesAlreadyInPartyWithoutNavigating()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, StateJson(Conan));
        var cut = Render(handler);

        cut.Find("input#join-id").Change(Id.ToString());
        cut.Find("button#join-party").Click();

        cut.WaitForAssertion(() => cut.Find("#join-members").TextContent.Should().Contain("Conan").And.Contain("Warrior"));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}");
        new Uri(Nav.Uri).AbsolutePath.Should().Be("/");
        _session.HasParty.Should().BeFalse();
    }

    [Fact]
    public void Join_Confirm_PostsChosenNameAndClass_AndRemembersMyHero()
    {
        var joined = false;
        var handler = new FakeHandler(HttpStatusCode.OK, bodyFactory: () => joined
            ? $$"""{"memberName":"Zed","state":{{StateJson($"{Conan},{Zed}")}}}"""
            : StateJson(Conan));
        var cut = Render(handler);

        cut.Find("input#join-id").Change(" k7f2-9qxm ");
        cut.Find("button#join-party").Click();
        cut.WaitForElement("#join-preview");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/parties/by-code/k7f2-9qxm");

        joined = true;
        cut.Find("input#join-name").Change("Zed");
        cut.Find("select#join-class").Change("Mage");
        cut.Find("button#confirm-join").Click();

        cut.WaitForAssertion(() => Nav.Uri.Should().EndWith("/dungeon"));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/parties/by-code/K7F2-9QXM/join");
        handler.LastRequestBody.Should().Be("""{"name":"Zed","class":"mage"}""");
        _session.PartyId.Should().Be(Id);
        _session.InviteCode.Should().Be("K7F2-9QXM");
        _session.PlayerName.Should().Be("Zed");
        _session.PlayerClass.Should().Be(DungeonForge.Web.Models.CharacterClass.Mage);
    }

    [Fact]
    public void Join_Confirm_WithoutName_ShowsErrorAndDoesNotPost()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, StateJson(Conan));
        var cut = Render(handler);
        cut.Find("input#join-id").Change(Id.ToString());
        cut.Find("button#join-party").Click();
        cut.WaitForElement("#join-preview");

        cut.Find("button#confirm-join").Click();

        cut.Find(".join-error").TextContent.Should().Contain("hero name");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}");
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
