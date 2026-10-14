using System.Net;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class DungeonApiClientTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly IReadOnlyList<MemberRequest> Roster = [new("Conan", CharacterClass.Warrior)];

    [Fact]
    public async Task CreateParty_Success_ReturnsPartyAndPostsCamelCaseBody()
    {
        var handler = new FakeHandler(HttpStatusCode.Created,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":[{"name":"A","class":"warrior","hp":30,"maxHp":30,"isDefeated":false},{"name":"B","class":"mage","hp":16,"maxHp":16,"isDefeated":false}],"createdAt":"2026-01-01T00:00:00+00:00"}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.CreatePartyAsync("Heroes", Roster);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.PartyId.Should().Be(Id);
        result.Value.Name.Should().Be("Heroes");
        result.Value.Members.Select(m => m.Name).Should().Equal("A", "B");
        result.Value.Members[1].Class.Should().Be(CharacterClass.Mage);
        result.Value.Members[1].MaxHp.Should().Be(16);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/parties");
        handler.LastRequestBody.Should().Contain("\"name\":\"Heroes\"").And.Contain("\"members\":[{\"name\":\"Conan\",\"class\":\"warrior\"}]");
    }

    [Fact]
    public async Task GetParty_Success_ReturnsState()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":[{"name":"A","class":"warrior","hp":9,"maxHp":30,"isDefeated":false}],"createdAt":"2026-01-01T00:00:00+00:00","gold":12,"turn":3,"isPartyDefeated":false}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.GetPartyAsync(Id);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.Members[0].Hp.Should().Be(9);
        result.Value.Turn.Should().Be(3);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}");
    }

    [Fact]
    public async Task CreateParty_BadRequest_MapsToValidationErrorWithMessage()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"name":["Name is required."]}}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.CreatePartyAsync("", Roster);

        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Value.Should().BeNull();
        result.Error.Should().Contain("Name is required.");
    }

    [Fact]
    public async Task CreateParty_BadRequestWithUnparseableBody_FallsBackToGenericMessage()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.BadRequest, "nope").CreateClient());

        var result = await sut.CreatePartyAsync("", Roster);

        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Explore_Success_DeserializesCamelCaseEnum()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":1,"roomType":"chest","targetName":"A","damage":3,"goldFound":12,"flavorEvent":"A chest!","gold":12,"isPartyDefeated":false}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.ExploreAsync(Id);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.RoomType.Should().Be(RoomType.Chest);
        result.Value.TargetName.Should().Be("A");
        result.Value.Damage.Should().Be(3);
        result.Value.GoldFound.Should().Be(12);
        result.Value.FlavorEvent.Should().Be("A chest!");
        result.Value.IsPartyDefeated.Should().BeFalse();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}/explore");
    }

    [Fact]
    public async Task Explore_NoActions_SendsNoBody_AndReadsScenario()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"Troll\nBridge","gold":0,"isPartyDefeated":false,"scenario":"Troll\nBridge","scenarioPending":false}""");

        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id);

        handler.LastRequestBody.Should().BeNull();
        result.Value!.Scenario.Should().Be("Troll\nBridge");
        result.Value.ScenarioPending.Should().BeFalse();
        result.Value.TargetName.Should().BeNull();
        result.Value.LastOutcome.Should().BeNull();
    }

    [Fact]
    public async Task Explore_EmptyActions_SendsNoBody()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"x","gold":0,"isPartyDefeated":false,"scenarioPending":true}""");

        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id, []);

        handler.LastRequestBody.Should().BeNull();
        result.Value!.ScenarioPending.Should().BeTrue();
        result.Value.Scenario.Should().BeNull();
    }

    [Fact]
    public async Task Explore_Puzzle_DeserializesRoomDiscussionAndNarrationPending()
    {
        var a = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"puzzle","damage":0,"goldFound":0,"flavorEvent":"Runes","gold":0,"isPartyDefeated":false,"scenario":"Runes","discussion":[{"memberId":"{{a}}","name":"Conan","text":"Press the left rune"}],"narrationPending":true}""");

        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id);

        result.Value!.RoomType.Should().Be(RoomType.Puzzle);
        result.Value.NarrationPending.Should().BeTrue();
        result.Value.Discussion.Should().ContainSingle().Which.Should().Be(new PlayerAction(a, "Conan", "Press the left rune"));
    }

    [Fact]
    public async Task Explore_OmittedOptionalFields_DefaultFalseAndNull()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":1,"roomType":"chest","damage":0,"goldFound":1,"flavorEvent":"x","gold":1,"isPartyDefeated":false}""");
        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id);
        result.Value!.NarrationPending.Should().BeFalse();
        result.Value.ScenarioPending.Should().BeFalse();
        result.Value.Scenario.Should().BeNull();
        result.Value.Discussion.Should().BeNull();
    }

    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Explore_Resolve_PostsActionsOnly()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":1,"roomType":"puzzle","damage":0,"goldFound":0,"flavorEvent":"R","gold":0,"isPartyDefeated":false,"narrationPending":true}""");

        await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id, [new PlayerAction(Actor, "Conan", "Pull lever")]);

        handler.LastRequestBody.Should().Be("{\"actions\":[{\"memberId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Conan\",\"text\":\"Pull lever\"}]}");
        handler.LastRequestBody.Should().NotContain("selectedChoiceIndex").And.NotContain("submitOnly");
    }

    [Fact]
    public async Task Explore_SubmitOnly_PostsActionsAndSubmitOnlyTrue()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"puzzle","damage":0,"goldFound":0,"flavorEvent":"R","gold":0,"isPartyDefeated":false,"scenario":"R"}""");

        await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id, [new PlayerAction(Actor, "Conan", "Pull lever")], submitOnly: true);

        handler.LastRequestBody.Should().Be("{\"actions\":[{\"memberId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Conan\",\"text\":\"Pull lever\"}],\"submitOnly\":true}");
    }

    [Fact]
    public async Task Explore_BadRequest_MapsToValidationError()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, """{"errors":{"actions":["At least one action is required."]}}""");
        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id, [new PlayerAction(Actor, "A", "x")]);
        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Error.Should().Contain("At least one action is required.");
    }

    [Fact]
    public async Task Explore_Combat_DeserializesCombatRoom()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":2,"roomType":"combat","targetName":"A","damage":5,"goldFound":0,"flavorEvent":"Fight","gold":0,"isPartyDefeated":false}""");
        var result = await new DungeonApiClient(handler.CreateClient()).ExploreAsync(Id);

        result.Value!.RoomType.Should().Be(RoomType.Combat);
    }

    [Fact]
    public async Task Explore_NotFound_MapsToNotFound()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.NotFound).CreateClient());
        var result = await sut.ExploreAsync(Id);
        result.Status.Should().Be(ApiStatus.NotFound);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Explore_Conflict_MapsToConflict()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.Conflict).CreateClient());
        var result = await sut.ExploreAsync(Id);
        result.Status.Should().Be(ApiStatus.Conflict);
    }

    [Fact]
    public async Task Explore_ServerError_MapsToFailed()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.InternalServerError).CreateClient());
        var result = await sut.ExploreAsync(Id);
        result.Status.Should().Be(ApiStatus.Failed);
    }

    [Fact]
    public async Task Explore_NetworkError_MapsToFailedInsteadOfThrowing()
    {
        var sut = new DungeonApiClient(new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:5062") });
        var result = await sut.ExploreAsync(Id);
        result.Status.Should().Be(ApiStatus.Failed);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }
}
