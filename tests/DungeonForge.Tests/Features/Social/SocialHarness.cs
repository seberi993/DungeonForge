using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Features.Parties;
using DungeonForge.Tests.Features.Parties.Chaos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Social;

// A derived factory with a manual clock and a recording notifier, plus the calls every social test makes.
internal sealed class SocialHarness : IDisposable
{
    public static readonly string[] Four = ["Conan", "Merlin", "Lyra", "Dru"];

    private readonly WebApplicationFactory<Program> _factory;

    private SocialHarness(WebApplicationFactory<Program> factory, ChaosRecordingNotifier notifier, ManualTime clock)
    {
        _factory = factory;
        Notifier = notifier;
        Clock = clock;
        Client = factory.CreateClient();
    }

    public ChaosRecordingNotifier Notifier { get; }

    public ManualTime Clock { get; }

    public HttpClient Client { get; }

    public static SocialHarness Create(
        WebApplicationFactory<Program> factory, Dictionary<string, string?>? config = null, IRoller? roller = null, Action<IServiceCollection>? services = null)
    {
        var notifier = new ChaosRecordingNotifier();
        var clock = new ManualTime(DateTimeOffset.UtcNow);
        var derived = factory.WithWebHostBuilder(b =>
        {
            if (config is not null)
            {
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config));
            }

            b.ConfigureServices(s =>
            {
                s.AddSingleton<IPartyNotifier>(notifier);
                s.AddSingleton<TimeProvider>(clock);
                if (roller is not null) s.AddSingleton(roller);
                services?.Invoke(s);
            });
        });
        return new SocialHarness(derived, notifier, clock);
    }

    public Task<Guid> PartyAsync(object[]? members = null) =>
        PartyTestData.CreatePartyAsync(Client, "Heroes", members ?? PartyTestBuilders.FourMembers);

    public T Get<T>() where T : notnull => _factory.Services.GetRequiredService<T>();

    public Party Party(Guid id) => PartyTestBuilders.CachedParty(_factory, id);

    public PlayerProfile Profile(Guid id, string name) => Party(id).Social().Profile(name);

    public Task<HttpResponseMessage> SeedAsync(Guid id, string name, string answer) =>
        Client.PostAsJsonAsync($"/api/parties/{id}/social/seed", new { playerName = name, answer });

    public async Task SeedAllAsync(Guid id, params string[] names)
    {
        var answers = new[]
        {
            "I would fight at the door and protect everyone",
            "I read the ancient runes and plan carefully",
            "I help the wounded and share what I have",
            "I trick them, sneak past, and pocket the gold",
        };
        for (var i = 0; i < names.Length; i++)
        {
            (await SeedAsync(id, names[i], answers[i % answers.Length])).EnsureSuccessStatusCode();
        }
    }

    public async Task<JsonDocument> MeAsync(Guid id, string name)
    {
        var response = await Client.GetAsync($"/api/parties/{id}/social/me?playerName={name}");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public Task<HttpResponseMessage> AskAsync(Guid id, string from, string[] to, string text, string kind = "question", bool compel = false) =>
        Client.PostAsJsonAsync($"/api/parties/{id}/social/asks", new { from, to, text, kind, compel });

    public Task<HttpResponseMessage> RevealAsync(Guid id, string askId, string from) =>
        Client.PostAsJsonAsync($"/api/parties/{id}/social/asks/{askId}/reveal", new { from });

    public Task<HttpResponseMessage> ReplyAsync(Guid id, string askId, string from, string choice, string? text = null) =>
        Client.PostAsJsonAsync($"/api/parties/{id}/social/asks/{askId}/reply", new { from, choice, text });

    public static async Task<string> AskIdAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("askId").GetString()!;
    }

    public IEnumerable<string> PrivateTexts(string player, string kind) =>
        Notifier.Privates.Where(p => p.Player == player && p.Message.Kind == kind).Select(p => p.Message.Text);

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}
