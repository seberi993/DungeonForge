using System.Net.Http.Json;
using System.Text.Json;

namespace DungeonForge.Tests.Features.Parties;

internal static class PartyTestData
{
    public static object[] DefaultMembers =>
    [
        new { name = "Conan", @class = "warrior" },
        new { name = "Merlin", @class = "mage" },
        new { name = "Mercy", @class = "priest" },
    ];

    public static async Task<HttpResponseMessage> PostPartyAsync(HttpClient client, string name = "Heroes", object[]? members = null) =>
        await client.PostAsJsonAsync("/api/parties", new { name, members = members ?? DefaultMembers });

    public static async Task<Guid> CreatePartyAsync(HttpClient client, string name = "Heroes", object[]? members = null)
    {
        var created = await PostPartyAsync(client, name, members);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("partyId").GetGuid();
    }
}
