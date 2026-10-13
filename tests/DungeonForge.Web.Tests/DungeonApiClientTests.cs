using System.Net;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class DungeonApiClientTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateParty_Success_ReturnsPartyAndPostsCamelCaseBody()
    {
        var handler = new FakeHandler(HttpStatusCode.Created,
            $$"""{"partyId":"{{Id}}","name":"Heroes","members":["A","B"],"createdAt":"2026-01-01T00:00:00+00:00"}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.CreatePartyAsync("Heroes");

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.PartyId.Should().Be(Id);
        result.Value.Name.Should().Be("Heroes");
        result.Value.Members.Should().Equal("A", "B");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/parties");
        handler.LastRequestBody.Should().Contain("\"name\":\"Heroes\"");
    }

    [Fact]
    public async Task CreateParty_BadRequest_MapsToValidationErrorWithMessage()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"name":["Name is required."]}}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.CreatePartyAsync("");

        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Value.Should().BeNull();
        result.Error.Should().Contain("Name is required.");
    }

    [Fact]
    public async Task CreateParty_BadRequestWithUnparseableBody_FallsBackToGenericMessage()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.BadRequest, "nope").CreateClient());

        var result = await sut.CreatePartyAsync("");

        result.Status.Should().Be(ApiStatus.ValidationError);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Explore_Success_DeserializesCamelCaseEnum()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":1,"roomType":"chest","hpChange":-3,"goldFound":12,"flavorEvent":"A chest!","hp":17,"gold":12}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.ExploreAsync(Id);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.RoomType.Should().Be(RoomType.Chest);
        result.Value.HpChange.Should().Be(-3);
        result.Value.GoldFound.Should().Be(12);
        result.Value.FlavorEvent.Should().Be("A chest!");
        result.Value.Hp.Should().Be(17);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}/explore");
    }

    [Fact]
    public async Task Explore_Combat_DeserializesCombatRoom()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":2,"roomType":"combat","hpChange":-5,"goldFound":0,"flavorEvent":"Fight","hp":12,"gold":0}""");
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
