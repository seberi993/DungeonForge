using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

// Client narration: the browser's local model writes the text and submits it; the server narrator is only the safety net.
public sealed class ClientNarrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TurnJson = "{\"outcome\":\"Bork swings wildly. Of course.\",\"next\":\"A **door** looms. What do you do?\",\"scene\":\"door\"}";

    private sealed class CountingNarrator : IDungeonNarrator
    {
        private int _scenarioCalls;
        private int _turnCalls;

        public int ScenarioCalls => Volatile.Read(ref _scenarioCalls);
        public int TurnCalls => Volatile.Read(ref _turnCalls);

        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _scenarioCalls);
            return Task.FromResult("Server scenario. What do you do?");
        }

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _turnCalls);
            return Task.FromResult(new TurnNarration("Server outcome.", "Server next. What do you do?"));
        }
    }

    private (HttpClient Client, CapturingNotifier Notifier, CountingNarrator Narrator, WebApplicationFactory<Program> Scope) Arrange(
        double deadlineSeconds = 30, bool clientNarration = true)
    {
        var notifier = new CapturingNotifier();
        var narrator = new CountingNarrator();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IPartyNotifier>(notifier);
            s.AddSingleton<IRoller>(new FixedRoller(0, Succeed));
            s.AddSingleton<IDungeonNarrator>(narrator);
            s.Configure<NarrationOptions>(o =>
            {
                o.ClientNarration = clientNarration;
                o.ClientDeadlineSeconds = deadlineSeconds;
            });
        }));
        return (derived.CreateClient(), notifier, narrator, derived);
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid id, int turn, string? text = null, bool declined = false) =>
        client.PostAsJsonAsync($"/api/parties/{id}/narration", new { turn, text, declined });

    private static async Task<JsonDocument> StartAsync(HttpClient client, Guid id)
    {
        using var first = await ReadAsync(await PromptAsync(client, id));
        first.RootElement.GetProperty("scenarioPending").GetBoolean().Should().BeTrue();
        (await SubmitAsync(client, id, 0, "A **goblin** waits. What do you do?")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return await PromptWhenReadyAsync(client, id);
    }

    [Fact]
    public async Task FirstPrompt_ReturnsABriefToTheClaimant_AndNeverCallsTheServerNarrator()
    {
        var (client, _, narrator, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        var brief = doc.RootElement.GetProperty("brief");
        brief.GetProperty("turn").GetInt32().Should().Be(0);
        brief.GetProperty("kind").GetString().Should().Be("scenario");
        brief.GetProperty("system").GetString().Should().Contain("1 to 2 short sentences");
        brief.GetProperty("user").GetString().Should().Contain("Party:");
        doc.RootElement.GetProperty("scenarioPending").GetBoolean().Should().BeTrue();
        narrator.ScenarioCalls.Should().Be(0);
    }

    [Fact]
    public async Task SubmittedScenario_IsStoredBroadcastAndServedByThePrompt_ExactlyOnce()
    {
        var (client, notifier, narrator, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var prompt = await StartAsync(client, id);

        prompt.RootElement.GetProperty("scenario").GetString().Should().Be("A **goblin** waits. What do you do?");
        (await notifier.FirstScenarioAsync).NextScenario.Should().Be("A **goblin** waits. What do you do?");
        (await SubmitAsync(client, id, 0, "another")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        narrator.ScenarioCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_ReturnsATurnBrief_AndTheSubmittedJsonBecomesTheNarration()
    {
        var (client, notifier, narrator, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);

        using var resolved = await ReadAsync(await ResolveAsync(client, id));

        var brief = resolved.RootElement.GetProperty("brief");
        brief.GetProperty("kind").GetString().Should().Be("turn");
        brief.GetProperty("turn").GetInt32().Should().Be(1);
        brief.GetProperty("jsonSchema").GetString().Should().Contain("outcome");
        brief.GetProperty("user").GetString().Should().Contain("Member inputs");
        narrator.TurnCalls.Should().Be(0);

        (await SubmitAsync(client, id, 1, TurnJson)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ready = await notifier.NarrationAsync;
        ready.Outcome.Should().Be("Bork swings wildly. Of course.");
        ready.NextScenario.Should().Be("A **door** looms. What do you do?");
        ready.NextScene.Should().Be("door");
        ready.Source.Should().Be("model");

        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("scenario").GetString().Should().Be("A **door** looms. What do you do?");
        (await SubmitAsync(client, id, 1, TurnJson)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task Brief_IsNotBroadcastToTheRestOfTheParty()
    {
        var (client, notifier, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);

        using var _r = await ReadAsync(await ResolveAsync(client, id));

        notifier.Turns.Should().ContainSingle().Which.Brief.Should().BeNull();
    }

    [Fact]
    public async Task Submit_WrongTurnOrUnknownParty_IsRejected()
    {
        var (client, _, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await PromptAsync(client, id);

        (await SubmitAsync(client, id, 7, "x")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SubmitAsync(client, Guid.NewGuid(), 0, "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SubmitAsync(client, id, 0, new string('x', 4001))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Declined_HandsTheTurnToTheServerNarratorAtOnce()
    {
        var (client, notifier, narrator, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);
        using var _r = await ReadAsync(await ResolveAsync(client, id));

        (await SubmitAsync(client, id, 1, declined: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ready = await notifier.NarrationAsync;
        ready.Outcome.Should().Be("Server outcome.");
        ready.Source.Should().Be("server");
        narrator.TurnCalls.Should().Be(1);
    }

    [Fact]
    public async Task GarbageModelText_FallsBackToTheServerNarratorInsteadOfCanningTheTurn()
    {
        var (client, notifier, narrator, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);
        using var _r = await ReadAsync(await ResolveAsync(client, id));

        (await SubmitAsync(client, id, 1, "I cannot do that, sorry")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await notifier.NarrationAsync).Outcome.Should().Be("Server outcome.");
        narrator.TurnCalls.Should().Be(1);
    }

    [Fact]
    public async Task PartialModelJson_IsRepairedWithCannedFlavor_AndFlaggedAsFallback()
    {
        var (client, notifier, _, scope) = Arrange();
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);
        using var _r = await ReadAsync(await ResolveAsync(client, id));

        (await SubmitAsync(client, id, 1, "{\"outcome\":\"Only this.\"}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ready = await notifier.NarrationAsync;
        ready.Outcome.Should().Be("Only this.");
        ready.NextScenario.Should().NotBeNullOrWhiteSpace();
        ready.Source.Should().Be("fallback");
    }

    [Fact]
    public async Task Deadline_LetsTheServerNarratorTakeOverOnce_AndALateSubmitIsRejected()
    {
        var (client, notifier, narrator, scope) = Arrange(deadlineSeconds: 0.2);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        using var _p = await StartAsync(client, id);
        using var _r = await ReadAsync(await ResolveAsync(client, id));

        var ready = await notifier.NarrationAsync;

        ready.Outcome.Should().Be("Server outcome.");
        narrator.TurnCalls.Should().Be(1);
        (await SubmitAsync(client, id, 1, TurnJson)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var next = await PromptWhenReadyAsync(client, id);
        next.RootElement.GetProperty("scenario").GetString().Should().Be("Server next. What do you do?");
    }

    [Fact]
    public async Task WithClientNarrationOff_NoBriefIsIssued_AndTheServerNarratesInline()
    {
        var (client, _, narrator, scope) = Arrange(clientNarration: false);
        using var _s = scope;
        var id = await PartyTestData.CreatePartyAsync(client);

        using var doc = await ReadAsync(await PromptAsync(client, id));

        doc.RootElement.TryGetProperty("brief", out _).Should().BeFalse();
        narrator.ScenarioCalls.Should().Be(1);
        (await SubmitAsync(client, id, 0, "x")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
