using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Narration;

namespace DungeonForge.Tests.Support;

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

/// <summary>Records every public and private message so tests can inspect exactly what leaked where.</summary>
internal sealed class ChaosRecordingNotifier : IPartyNotifier
{
    private readonly List<string> _public = [];

    public List<(Guid PartyId, string Player, PrivateMessage Message)> Privates { get; } = [];
    public List<ChaosEvent> Events { get; } = [];
    public List<ExploreResponse> Turns { get; } = [];
    public int DiscussionBroadcasts { get; private set; }

    /// <summary>Serialized text of every public broadcast, for leak assertions.</summary>
    public string PublicTraffic { get { lock (_public) { return string.Join("\n", _public); } } }

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private void Pub(object o) { lock (_public) { _public.Add(JsonSerializer.Serialize(o, o.GetType(), Json)); } }

    public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) { Pub(state); return Task.CompletedTask; }

    public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default)
    {
        Pub(turn);
        lock (Turns) { Turns.Add(turn); }
        return Task.CompletedTask;
    }

    public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) { Pub(narration); return Task.CompletedTask; }

    public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default)
    {
        DiscussionBroadcasts++;
        Pub(actions);
        return Task.CompletedTask;
    }

    public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) { Pub(status); return Task.CompletedTask; }

    public Task ChaosEventAsync(Guid partyId, ChaosEvent chaosEvent, CancellationToken ct = default)
    {
        Pub(chaosEvent);
        lock (Events) { Events.Add(chaosEvent); }
        return Task.CompletedTask;
    }

    public Task SendPrivateAsync(Guid partyId, string playerName, PrivateMessage message, CancellationToken ct = default)
    {
        lock (Privates) { Privates.Add((partyId, playerName, message)); }
        return Task.CompletedTask;
    }
}

internal sealed class CapturingNarrator : IDungeonNarrator
{
    public List<NarrationContext> Turns { get; } = [];

    public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
        Task.FromResult("A bone-carved hall. What do you do?");

    public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
    {
        lock (Turns) { Turns.Add(ctx); }
        return Task.FromResult(FallbackFlavor.Complete(null, success, ctx));
    }
}
