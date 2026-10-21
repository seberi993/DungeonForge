using DungeonForge.Infrastructure.Parties;
using System.Net.Http.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Scores;
using DungeonForge.Application.Narration;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static DungeonForge.Tests.Features.Parties.ExploreDungeonNarrationTests;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

// Every failure the explore handler swallows leaves one coded event naming the operation, the party and the turn.
public sealed class ExploreDungeonLoggingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private (HttpClient Client, LogCapture Logs, WebApplicationFactory<Program> Scope) Host(
        IDungeonNarrator? narrator = null, IPartyNotifier? notifier = null, IScoreBoard? board = null, int fail = Succeed)
    {
        var logs = new LogCapture();
        var scope = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddLogging(l => l.AddProvider(logs));
            s.AddSingleton<IRoller>(new FixedRoller(0, fail));
            if (narrator is not null) s.AddSingleton(narrator);
            if (notifier is not null) s.AddSingleton(notifier);
            if (board is not null) s.AddSingleton(board);
        }));
        _derived.Add(scope);
        return (scope.CreateClient(), logs, scope);
    }

    private static LogEntry Single(LogCapture logs, int eventId) =>
        logs.Entries.Should().ContainSingle(e => e.EventId.Id == eventId).Subject;

    private static async Task WaitForAsync(LogCapture logs, int eventId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!logs.Entries.Any(e => e.EventId.Id == eventId))
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"event {eventId} should have been logged");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task FirstScenarioBroadcast_HubDown_StillAnswers_AndLogsTheSend()
    {
        var (client, logs, _) = Host(notifier: new FailingNotifier());
        var id = await PartyTestData.CreatePartyAsync(client);

        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var entry = Single(logs, LogEvents.BroadcastFailedId);
        entry.Message.Should().Contain("FirstScenario");
        entry.Scope.Should().Contain("Operation", ExploreOperations.GetScenario).And.Contain("PartyId", id).And.Contain("Turn", 0);
    }

    [Fact]
    public async Task ResolveBroadcasts_HubDown_StillResolve_AndEachFailedSendIsLogged()
    {
        var (client, logs, _) = Host(notifier: new FailingNotifier());
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        logs.Entries.Clear();

        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();

        var sends = logs.Entries.Where(e => e.EventId.Id == LogEvents.BroadcastFailedId).Select(e => e.Message).ToList();
        sends.Should().Contain(m => m.Contains("TurnResolved")).And.Contain(m => m.Contains("PartyUpdated"))
            .And.Contain(m => m.Contains("TurnLockUpdated"));
        logs.Entries.Where(e => e.EventId.Id == LogEvents.BroadcastFailedId && e.Message.Contains("TurnResolved"))
            .Single().Scope.Should().Contain("Operation", ExploreOperations.ResolveTurn).And.Contain("PartyId", id).And.Contain("Turn", 1);
    }

    [Fact]
    public async Task NarrationReady_HubDown_IsLoggedFromTheBackgroundPhase()
    {
        var (client, logs, _) = Host(notifier: new FailingNotifier());
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!logs.Entries.Any(e => e.EventId.Id == LogEvents.BroadcastFailedId && e.Message.Contains("NarrationReady")
                                      && (string?)e.Scope["Operation"] == ExploreOperations.NarrateTurn))
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the background narration should have tried to push NarrationReady");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task NarratorThrowsForTheScenario_FallsBack_AndLogsIt()
    {
        var (client, logs, _) = Host(new FakeNarrator(throwOnScenario: true));
        var id = await PartyTestData.CreatePartyAsync(client);

        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        var entry = Single(logs, LogEvents.NarratorScenarioFailedId);
        entry.Exception.Should().BeOfType<InvalidOperationException>();
        entry.Scope.Should().Contain("Operation", ExploreOperations.GetScenario).And.Contain("PartyId", id);
    }

    [Fact]
    public async Task NarratorReturnsABlankScenario_FallsBack_AndLogsIt()
    {
        var (client, logs, _) = Host(new FakeNarrator(scenario: "  "));
        var id = await PartyTestData.CreatePartyAsync(client);

        (await PromptAsync(client, id)).EnsureSuccessStatusCode();

        Single(logs, LogEvents.NarratorEmptyScenarioId).Scope.Should().Contain("Operation", ExploreOperations.GetScenario);
    }

    [Fact]
    public async Task NarratorThrowsForTheTurn_FallsBack_AndLogsIt()
    {
        var (client, logs, _) = Host(new FakeNarrator(throwOnTurn: true));
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();

        await WaitForAsync(logs, LogEvents.NarratorTurnFailedId);

        var entry = Single(logs, LogEvents.NarratorTurnFailedId);
        entry.Scope.Should().Contain("Operation", ExploreOperations.NarrateTurn).And.Contain("PartyId", id).And.Contain("Turn", 0);
    }

    private sealed class DeadBoard : IScoreBoard
    {
        public Task RecordAsync(RunScore score, CancellationToken ct = default) => throw new HttpRequestException("board is down");

        public Task<IReadOnlyList<RunScore>> TopAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunScore>>([]);
    }

    [Fact]
    public async Task ScoreBoardDown_DoesNotBlockTheDefeat_AndIsLogged()
    {
        var (client, logs, _) = Host(board: new DeadBoard(), fail: Fail);
        var id = await PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);
        for (var i = 0; i < 3; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        var entry = Single(logs, LogEvents.ScoreRecordFailedId);
        entry.Scope.Should().Contain("Operation", ExploreOperations.ResolveTurn).And.Contain("PartyId", id);
    }

    [Fact]
    public async Task NarrationPhaseFault_IsLogged_AndTheTurnStillCompletes()
    {
        var logs = new LogCapture();
        using var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IRoller>(new FixedRoller(0, Succeed))));
        var id = await PartyTestData.CreatePartyAsync(derived.CreateClient());
        var cache = new FaultableCache(derived.Services.GetRequiredService<IMemoryCache>());
        var notifier = new CapturingNotifier();
        using var loggers = LoggerFactory.Create(b => b.AddProvider(logs));
        var handler = NewHandler(
            new PartyStore(cache), new FixedRoller(0, Succeed), new FaultArmingNarrator(cache), notifier, loggers);

        (await handler.HandleAsync(id, null)).IsSuccess.Should().BeTrue();
        (await handler.HandleAsync(id, new ExploreRequest(WholeParty()))).IsSuccess.Should().BeTrue();
        await notifier.NarrationAsync;

        var entry = Single(logs, LogEvents.NarrationPhaseFailedId);
        entry.Scope.Should().Contain("Operation", ExploreOperations.NarrateTurn).And.Contain("PartyId", id).And.Contain("Turn", 1);
    }
}
