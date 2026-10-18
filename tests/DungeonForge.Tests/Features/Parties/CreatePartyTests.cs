using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public class CreatePartyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Post_ValidNameAndMembers_ReturnsOkWithPartyAndMemberDetails()
    {
        var response = await PartyTestData.PostPartyAsync(factory.CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadJson(response);
        var root = doc.RootElement;
        root.GetProperty("partyId").GetGuid().Should().NotBeEmpty();
        root.GetProperty("name").GetString().Should().Be("Heroes");
        root.GetProperty("createdAt").GetDateTimeOffset().Should().NotBe(default);

        var members = root.GetProperty("members");
        members.GetArrayLength().Should().Be(3);
        var warrior = members[0];
        warrior.GetProperty("name").GetString().Should().Be("Conan");
        warrior.GetProperty("class").GetString().Should().Be("warrior");
        warrior.GetProperty("hp").GetInt32().Should().Be(30);
        warrior.GetProperty("maxHp").GetInt32().Should().Be(30);
        warrior.GetProperty("isDefeated").GetBoolean().Should().BeFalse();
        members[1].GetProperty("class").GetString().Should().Be("mage");
        members[1].GetProperty("maxHp").GetInt32().Should().Be(16);
        members[2].GetProperty("class").GetString().Should().Be("priest");
        members[2].GetProperty("maxHp").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task Post_ValidParty_DoesNotExposePartyLevelHp()
    {
        var response = await PartyTestData.PostPartyAsync(factory.CreateClient());

        using var doc = await ReadJson(response);
        doc.RootElement.TryGetProperty("hp", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Post_Twice_ReturnsDifferentPartyIds()
    {
        var client = factory.CreateClient();

        using var first = await ReadJson(await PartyTestData.PostPartyAsync(client, "A"));
        using var second = await ReadJson(await PartyTestData.PostPartyAsync(client, "B"));

        first.RootElement.GetProperty("partyId").GetGuid()
            .Should().NotBe(second.RootElement.GetProperty("partyId").GetGuid());
    }

    [Fact]
    public async Task Post_ValidParty_StoresPartyInMemoryCache()
    {
        var response = await PartyTestData.PostPartyAsync(factory.CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadJson(response);
        var id = doc.RootElement.GetProperty("partyId").GetGuid();

        var cache = factory.Services.GetRequiredService<IMemoryCache>();
        cache.TryGetValue($"party:{id}", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankName_ReturnsBadRequestAndCachesNothing(string name)
    {
        var cache = factory.Services.GetRequiredService<IMemoryCache>();
        var before = (cache as MemoryCache)?.Count ?? 0;

        var response = await PartyTestData.PostPartyAsync(factory.CreateClient(), name);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((cache as MemoryCache)?.Count ?? 0).Should().Be(before);
    }

    [Fact]
    public async Task Post_MembersMissing_ReturnsBadRequestAndCachesNothing()
    {
        var cache = factory.Services.GetRequiredService<IMemoryCache>();
        var before = (cache as MemoryCache)?.Count ?? 0;

        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties", new { name = "Heroes" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((cache as MemoryCache)?.Count ?? 0).Should().Be(before);
    }

    [Fact]
    public async Task Post_EmptyMembers_ReturnsBadRequestAndCachesNothing()
    {
        var cache = factory.Services.GetRequiredService<IMemoryCache>();
        var before = (cache as MemoryCache)?.Count ?? 0;

        var response = await PartyTestData.PostPartyAsync(factory.CreateClient(), members: []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((cache as MemoryCache)?.Count ?? 0).Should().Be(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankMemberName_ReturnsBadRequest(string memberName)
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = memberName, @class = "warrior" }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MemberNameMissing_ReturnsBadRequest()
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { @class = "warrior" }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MemberClassMissing_DealsAHiddenClass()
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = "Conan" }]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("dragon")]
    [InlineData("")]
    public async Task Post_UnknownMemberClass_ReturnsBadRequest(string cls)
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = "Conan", @class = cls }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_DuplicateMemberNames_CaseInsensitiveAfterTrim_ReturnsBadRequest()
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = "Conan", @class = "warrior" }, new { name = "  conan ", @class = "mage" }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("members");
    }

    [Fact]
    public async Task Post_MemberNameTooLong_ReturnsBadRequest()
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = new string('a', 33), @class = "warrior" }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_MemberNameAtLimit_ReturnsOk()
    {
        var response = await PartyTestData.PostPartyAsync(
            factory.CreateClient(), members: [new { name = new string('a', 32), @class = "warrior" }]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_PartyNameTooLong_ReturnsBadRequest()
    {
        var response = await PartyTestData.PostPartyAsync(factory.CreateClient(), name: new string('p', 41));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_TooManyMembers_ReturnsBadRequest()
    {
        object[] members = [.. Enumerable.Range(1, 9).Select(i => (object)new { name = $"Hero{i}", @class = "warrior" })];

        var response = await PartyTestData.PostPartyAsync(factory.CreateClient(), members: members);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_EightMembers_ReturnsOk()
    {
        object[] members = [.. Enumerable.Range(1, 8).Select(i => (object)new { name = $"Hero{i}", @class = "warrior" })];

        var response = await PartyTestData.PostPartyAsync(factory.CreateClient(), members: members);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
