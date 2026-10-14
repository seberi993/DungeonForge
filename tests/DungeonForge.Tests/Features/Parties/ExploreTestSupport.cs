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

/// <summary>Room rolls come from a queue (last value repeats); any other max is the success roll.</summary>
internal sealed class SequenceRoller(int success, params int[] rooms) : IRoller
{
    private int _i;

    public int Next(int max) => max == 3 ? rooms[Math.Min(Interlocked.Increment(ref _i) - 1, rooms.Length - 1)] : success;
}

internal static class ExploreTestSupport
{
    /// <summary>Success roll value that always fails (base 55 + max bonus 15 = 70 &lt; 99).</summary>
    public const int Fail = 99;

    /// <summary>Success roll value that always succeeds.</summary>
    public const int Succeed = 0;

    public static readonly Guid DefaultMember = Guid.NewGuid();

    /// <summary>Stable member ids of the default three-member party (Conan, Merlin, Mercy).</summary>
    public static readonly Guid ConanId = DefaultMember;
    public static readonly Guid MerlinId = Guid.NewGuid();
    public static readonly Guid MercyId = Guid.NewGuid();

    /// <summary>One action per default party member: submitting these locks the whole party in.</summary>
    public static PlayerAction[] WholeParty() =>
    [
        Act("We press forward carefully", "Conan", ConanId),
        Act("I weave a ward", "Merlin", MerlinId),
        Act("I steady the line", "Mercy", MercyId),
    ];

    public static PlayerAction Act(string text = "We press forward carefully", string name = "Conan", Guid? member = null) =>
        new(member ?? DefaultMember, name, text);

    public static Task<HttpResponseMessage> PromptAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/api/parties/{id}/explore", null);

    /// <summary>Resolve step: aggregated actions (defaults to one action).</summary>
    public static Task<HttpResponseMessage> ResolveAsync(HttpClient client, Guid id, params PlayerAction[] actions) =>
        client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions = actions.Length == 0 ? WholeParty() : actions });

    /// <summary>Discussion-only submit: no turn change.</summary>
    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid id, params PlayerAction[] actions) =>
        client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions, submitOnly = true });

    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    /// <summary>Prompts until the scenario exists (the previous turn's narration may still be in flight).</summary>
    public static async Task<JsonDocument> PromptWhenReadyAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var response = await PromptAsync(client, id);
            response.EnsureSuccessStatusCode();
            var doc = await ReadAsync(response);
            if (!doc.RootElement.GetProperty("scenarioPending").GetBoolean())
            {
                return doc;
            }

            doc.Dispose();
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Scenario never became available.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>Waits for the scenario, then resolves with one action; returns the resolved turn payload.</summary>
    public static async Task<JsonDocument> TurnAsync(HttpClient client, Guid id)
    {
        (await PromptWhenReadyAsync(client, id)).Dispose();
        var resolved = await ResolveAsync(client, id);
        resolved.EnsureSuccessStatusCode();
        return await ReadAsync(resolved);
    }
}

/// <summary>Notifier that records everything and lets tests await the asynchronous narration phase.</summary>
internal sealed class CapturingNotifier : IPartyNotifier
{
    private readonly TaskCompletionSource<NarrationReady> _narration = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<ExploreResponse> Turns { get; } = [];
    public List<IReadOnlyList<PlayerAction>> Discussions { get; } = [];
    public List<(Guid PartyId, IReadOnlyList<LockStatusDto> Status)> LockUpdates { get; } = [];

    private readonly TaskCompletionSource<NarrationReady> _firstScenario = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>First turn-resolution narration (non-empty outcome).</summary>
    public Task<NarrationReady> NarrationAsync => _narration.Task.WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>First-turn scenario broadcast (empty outcome).</summary>
    public Task<NarrationReady> FirstScenarioAsync => _firstScenario.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default)
    {
        lock (Turns) { Turns.Add(turn); }
        return Task.CompletedTask;
    }

    public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default)
    {
        (narration.Outcome.Length == 0 ? _firstScenario : _narration).TrySetResult(narration);
        return Task.CompletedTask;
    }

    public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default)
    {
        lock (Discussions) { Discussions.Add(actions); }
        return Task.CompletedTask;
    }

    public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default)
    {
        lock (LockUpdates) { LockUpdates.Add((partyId, status)); }
        return Task.CompletedTask;
    }
}
