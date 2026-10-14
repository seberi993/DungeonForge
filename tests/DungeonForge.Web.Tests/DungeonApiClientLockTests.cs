using System.Net;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;

namespace DungeonForge.Web.Tests;

public class DungeonApiClientLockTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetSpells_ParsesBareArray_AndUsesCamelCaseClassPath()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            """[{"name":"Smite","classId":"paladin","element":"holy","manaCost":5,"description":"Holy blow."}]""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.GetSpellsAsync(CharacterClass.Paladin);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value.Should().ContainSingle();
        result.Value![0].Should().Be(new SpellDto("Smite", CharacterClass.Paladin, ElementType.Holy, 5, "Holy blow."));
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/classes/paladin/spells");
    }

    [Fact]
    public async Task Unlock_PostsMemberId_AndParsesLockStatus()
    {
        var member = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK, $$"""{"partyId":"{{Id}}","lockStatus":[{"name":"A","lockedIn":false}]}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.UnlockAsync(Id, member);

        result.Status.Should().Be(ApiStatus.Success);
        result.Value!.LockStatus.Should().ContainSingle().Which.Should().Be(new LockStatusDto("A", false));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/api/parties/{Id}/unlock");
        handler.LastRequestBody.Should().Be($"{{\"memberId\":\"{member}\"}}");
    }

    [Fact]
    public async Task Explore_Waiting409_MapsToWaiting_NotConflict()
    {
        var handler = new FakeHandler(HttpStatusCode.Conflict,
            $$"""{"partyId":"{{Id}}","locked":1,"required":2,"lockStatus":[],"status":"waiting"}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.ExploreAsync(Id, [new PlayerAction(Guid.NewGuid(), "A", "hit")]);

        result.Status.Should().Be(ApiStatus.Waiting);
    }

    [Fact]
    public async Task Explore_DefeatedConflict_StaysConflict()
    {
        var sut = new DungeonApiClient(new FakeHandler(HttpStatusCode.Conflict, "").CreateClient());
        (await sut.ExploreAsync(Id)).Status.Should().Be(ApiStatus.Conflict);
    }

    [Fact]
    public async Task Explore_ResponseCarriesLockStatus()
    {
        var handler = new FakeHandler(HttpStatusCode.OK,
            $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"x","gold":0,"isPartyDefeated":false,"scenario":"s","lockStatus":[{"name":"A","lockedIn":true}]}""");
        var sut = new DungeonApiClient(handler.CreateClient());

        var result = await sut.ExploreAsync(Id);

        result.Value!.LockStatus.Should().ContainSingle().Which.LockedIn.Should().BeTrue();
    }
}
