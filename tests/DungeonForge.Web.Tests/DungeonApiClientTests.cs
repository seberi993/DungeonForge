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
