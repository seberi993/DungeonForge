using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties.Chaos;

/// <summary>Replays scripted values (modulo max); the last value repeats once the script is exhausted.</summary>
internal sealed class ScriptedRoller(params int[] values) : IRoller
{
    private int _i;

    public int Next(int max) => values[Math.Min(_i++, values.Length - 1)] % max;
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

internal static class ChaosTestSupport
{
    public static readonly PartyModifiers ChaosOn = new(Chaos: true);

    public static PartyMember[] Roster() =>
    [
        new("Conan", CharacterClass.Warrior),
        new("Merlin", CharacterClass.Mage),
        new("Lyra", CharacterClass.Bard),
        new("Dru", CharacterClass.Druid),
    ];

    public static Party NewParty(PartyModifiers? modifiers = null, params PartyMember[] members) =>
        Party.Create(Guid.NewGuid(), "Heroes", members.Length == 0 ? Roster() : members, DateTimeOffset.UtcNow, "", modifiers);

    public static async Task<Guid> CreateAsync(HttpClient client, PartyModifiers modifiers, params object[] members)
    {
        var body = new
        {
            name = "Heroes",
            members = members.Length == 0 ? PartyTestData.DefaultMembers : members,
            modifiers,
        };
        var response = await client.PostAsJsonAsync("/api/parties", body);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("partyId").GetGuid();
    }

    public static object[] FourMembers =>
    [
        new { name = "Conan", @class = "warrior" },
        new { name = "Merlin", @class = "mage" },
        new { name = "Lyra", @class = "bard" },
        new { name = "Dru", @class = "druid" },
    ];

    public static Party CachedParty(WebApplicationFactory<Program> f, Guid id) =>
        f.Services.GetRequiredService<IMemoryCache>().Get<Party>(PartyCache.Key(id))!;

    public static HubConnection Connect(WebApplicationFactory<Program> f) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(f.Server.BaseAddress, "/hubs/dungeon"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
            })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            })
            .Build();
}
