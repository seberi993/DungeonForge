using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;

namespace DungeonForge.Tests.Features.Parties;

/// <summary>Deterministic roller: Next(3) is the room roll (0 = combat, 1 = chest, 2 = puzzle); any other max is the success roll.</summary>
internal sealed class FixedRoller(int room, int success = 0) : IRoller
{
    public int Next(int max) => max == 3 ? room : success;
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

    public static Task<HttpResponseMessage> ActAsync(HttpClient client, Guid id, int? index, params PlayerAction[] actions) =>
        client.PostAsJsonAsync($"/api/parties/{id}/explore", new { selectedChoiceIndex = index, actions });
}

/// <summary>Notifier that records everything and lets tests await the asynchronous narration phase.</summary>
internal sealed class CapturingNotifier : IPartyNotifier
{
    private readonly TaskCompletionSource<NarrationReady> _narration = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<ExploreResponse> Turns { get; } = [];
    public List<IReadOnlyList<PlayerAction>> Discussions { get; } = [];

    public Task<NarrationReady> NarrationAsync => _narration.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default)
    {
        lock (Turns) { Turns.Add(turn); }
        return Task.CompletedTask;
    }

    public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default)
    {
        _narration.TrySetResult(narration);
        return Task.CompletedTask;
    }

    public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default)
    {
        lock (Discussions) { Discussions.Add(actions); }
        return Task.CompletedTask;
    }
}
