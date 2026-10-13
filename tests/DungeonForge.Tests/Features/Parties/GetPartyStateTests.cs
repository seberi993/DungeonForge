using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class GetPartyStateTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_ExistingParty_ReturnsOkWithPartyAndMemberDetails()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await client.GetAsync($"/api/parties/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("partyId").GetGuid().Should().Be(id);
        root.GetProperty("name").GetString().Should().Be("Heroes");
        root.GetProperty("createdAt").GetDateTimeOffset().Should().NotBe(default);
        root.GetProperty("isPartyDefeated").GetBoolean().Should().BeFalse();
        root.TryGetProperty("hp", out _).Should().BeFalse();

        var members = root.GetProperty("members");
        members.GetArrayLength().Should().Be(3);
        members[0].GetProperty("name").GetString().Should().Be("Conan");
        members[0].GetProperty("class").GetString().Should().Be("warrior");
        members[0].GetProperty("hp").GetInt32().Should().Be(30);
        members[0].GetProperty("maxHp").GetInt32().Should().Be(30);
        members[0].GetProperty("isDefeated").GetBoolean().Should().BeFalse();
        members[1].GetProperty("name").GetString().Should().Be("Merlin");
        members[2].GetProperty("name").GetString().Should().Be("Mercy");
    }

    [Fact]
    public async Task Get_UnknownPartyId_ReturnsNotFound()
    {
        var response = await factory.CreateClient().GetAsync($"/api/parties/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
