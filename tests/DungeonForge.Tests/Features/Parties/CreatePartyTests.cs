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
    public async Task Post_ValidName_ReturnsOkWithParty()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties", new { name = "Heroes" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadJson(response);
        var root = doc.RootElement;
        root.GetProperty("partyId").GetGuid().Should().NotBeEmpty();
        root.GetProperty("name").GetString().Should().Be("Heroes");
        root.GetProperty("members").GetArrayLength().Should().Be(0);
        root.GetProperty("createdAt").GetDateTimeOffset().Should().NotBe(default);
    }

    [Fact]
    public async Task Post_Twice_ReturnsDifferentPartyIds()
    {
        var client = factory.CreateClient();

        using var first = await ReadJson(await client.PostAsJsonAsync("/api/parties", new { name = "A" }));
        using var second = await ReadJson(await client.PostAsJsonAsync("/api/parties", new { name = "B" }));

        first.RootElement.GetProperty("partyId").GetGuid()
            .Should().NotBe(second.RootElement.GetProperty("partyId").GetGuid());
    }

    [Fact]
    public async Task Post_ValidName_StoresPartyInMemoryCache()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties", new { name = "Heroes" });

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

        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties", new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((cache as MemoryCache)?.Count ?? 0).Should().Be(before);
    }
}
