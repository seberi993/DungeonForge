using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class GetPartyStateTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_ExistingParty_ReturnsOkWithPartyDetails()
    {
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/parties", new { name = "Heroes" });
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetProperty("partyId").GetGuid();

        var response = await client.GetAsync($"/api/parties/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("partyId").GetGuid().Should().Be(id);
        root.GetProperty("name").GetString().Should().Be("Heroes");
        root.GetProperty("members").GetArrayLength().Should().Be(0);
        root.GetProperty("createdAt").GetDateTimeOffset().Should().NotBe(default);
    }

    [Fact]
    public async Task Get_UnknownPartyId_ReturnsNotFound()
    {
        var response = await factory.CreateClient().GetAsync($"/api/parties/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
