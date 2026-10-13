using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class JoinByCodeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<(Guid Id, string Code)> CreateAsync(HttpClient client, string name = "Heroes")
    {
        var created = await client.PostAsJsonAsync("/api/parties", new { name });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("partyId").GetGuid(), doc.RootElement.GetProperty("inviteCode").GetString()!);
    }

    [Fact]
    public async Task Create_ReturnsShortInviteCode()
    {
        var (_, code) = await CreateAsync(factory.CreateClient());
        code.Should().MatchRegex("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$");
    }

    [Fact]
    public async Task Create_GeneratesDistinctCodes()
    {
        var client = factory.CreateClient();
        var codes = new HashSet<string>();
        for (var i = 0; i < 20; i++) codes.Add((await CreateAsync(client)).Code);
        codes.Should().HaveCount(20);
    }

    [Fact]
    public async Task ByCode_ReturnsPartyState_CaseAndDashInsensitive()
    {
        var client = factory.CreateClient();
        var (id, code) = await CreateAsync(client, "Raiders");

        foreach (var variant in new[] { code, code.ToLowerInvariant(), code.Replace("-", ""), $" {code} " })
        {
            var response = await client.GetAsync($"/api/parties/by-code/{Uri.EscapeDataString(variant)}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, variant);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("partyId").GetGuid().Should().Be(id);
            doc.RootElement.GetProperty("name").GetString().Should().Be("Raiders");
            doc.RootElement.GetProperty("inviteCode").GetString().Should().Be(code);
        }
    }

    [Theory]
    [InlineData("ZZZZ-ZZZZ")]
    [InlineData("short")]
    [InlineData("0000-0000")]
    public async Task ByCode_UnknownOrMalformed_ReturnsNotFound(string code)
    {
        var response = await factory.CreateClient().GetAsync($"/api/parties/by-code/{code}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_IncludesInviteCode()
    {
        var client = factory.CreateClient();
        var (id, code) = await CreateAsync(client);

        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/parties/{id}"));
        doc.RootElement.GetProperty("inviteCode").GetString().Should().Be(code);
    }
}
