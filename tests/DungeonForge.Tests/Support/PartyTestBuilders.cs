using DungeonForge.Infrastructure.Scores;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Features.Scores;
using DungeonForge.Application.Narration;
using DungeonForge.Tests.Features.Parties;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Support;

/// <summary>Shared builders for the explore and chaos tests: handler wiring, parties, HTTP calls, hub connections.</summary>
internal static class PartyTestBuilders
{
    /// <summary>An explore handler wired the way DI wires it, from the pieces a test wants to control.</summary>
    public static ExploreDungeonHandler NewHandler(
        IPartyStore parties, IRoller roller, IDungeonNarrator narrator, IPartyNotifier notifier, ILoggerFactory? loggers = null)
    {
        loggers ??= NullLoggerFactory.Instance;
        var chaos = new ChaosDispatcher(notifier, new RandomRoller(), loggers.CreateLogger<ChaosDispatcher>());
        var vocabulary = new VocabularySelector(new RandomRoller());
        var turns = new TurnNarrator(parties, narrator, notifier, chaos, loggers.CreateLogger<TurnNarrator>());
        var clientNarration = new ClientNarrationCoordinator(
            parties, turns, Options.Create(new NarrationOptions()), loggers.CreateLogger<ClientNarrationCoordinator>());
        var getScenario = new GetScenarioHandler(parties, roller, turns, clientNarration, vocabulary);
        var resolveTurn = new ResolveTurnHandler(
            parties, new TurnResolver(parties, roller, vocabulary), turns, clientNarration, notifier, new InMemoryScoreBoard(), chaos,
            loggers.CreateLogger<ResolveTurnHandler>());
        return new ExploreDungeonHandler(getScenario, new LockInHandler(getScenario, resolveTurn), resolveTurn);
    }

    /// <summary>Success roll value that always fails without fumbling (above the 85% cap, below the fumble line).</summary>
    public const int Fail = 90;

    /// <summary>Success roll value that always succeeds without a crit (below the 15% floor, above the crit line).</summary>
    public const int Succeed = 10;

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
        f.Services.GetRequiredService<IPartyStore>().TryGet(id, out var party)
            ? party
            : throw new InvalidOperationException($"Party {id} is not stored.");

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
