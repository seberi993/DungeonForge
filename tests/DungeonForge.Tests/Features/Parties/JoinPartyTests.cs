using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class JoinPartyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<string> CreateCodeAsync(HttpClient client)
    {
        var created = await PartyTestData.PostPartyAsync(client);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("inviteCode").GetString()!;
    }

    [Fact]
    public async Task Join_AddsMemberWithChosenClass_AndReturnsFullRoster()
    {
        var client = factory.CreateClient();
        var code = await CreateCodeAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = " Zed ", @class = "necromancer" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("memberName").GetString().Should().Be("Zed");
        var members = doc.RootElement.GetProperty("state").GetProperty("members").EnumerateArray().ToList();
        members.Should().HaveCount(4);
        members[^1].GetProperty("name").GetString().Should().Be("Zed");
        members[^1].GetProperty("class").GetString().Should().Be("necromancer");
    }

    [Fact]
    public async Task Join_DuplicateName_ReturnsConflict()
    {
        var client = factory.CreateClient();
        var code = await CreateCodeAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = "conan", @class = "bard" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Join_UnknownCode_ReturnsNotFound()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties/by-code/ZZZZ-ZZZZ/join", new { name = "Zed", @class = "bard" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("", "bard")]
    [InlineData("Zed", null)]
    public async Task Join_InvalidBody_ReturnsBadRequest(string name, string? cls)
    {
        var client = factory.CreateClient();
        var code = await CreateCodeAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name, @class = cls });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
