using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Tests.Features.Parties;

/// <summary>Deterministic roller: Next(2) is the room roll (0 = combat, 1 = chest); any other max is the success roll.</summary>
internal sealed class FixedRoller(int room, int success = 0) : IRoller
{
    public int Next(int max) => max == 2 ? room : success;
}

internal static class ExploreTestSupport
{
    /// <summary>Success roll value that always fails every choice.</summary>
    public const int Fail = 99;

    /// <summary>Success roll value that always succeeds every choice.</summary>
    public const int Succeed = 0;

    public static Task<HttpResponseMessage> PromptAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/api/parties/{id}/explore", null);

    public static Task<HttpResponseMessage> ChooseAsync(HttpClient client, Guid id, int index) =>
        client.PostAsJsonAsync($"/api/parties/{id}/explore", new { selectedChoiceIndex = index });

    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    /// <summary>Prompt step followed by a choice; returns the resolved turn payload.</summary>
    public static async Task<JsonDocument> TurnAsync(HttpClient client, Guid id, int index = 0)
    {
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        var resolved = await ChooseAsync(client, id, index);
        resolved.EnsureSuccessStatusCode();
        return await ReadAsync(resolved);
    }
}
