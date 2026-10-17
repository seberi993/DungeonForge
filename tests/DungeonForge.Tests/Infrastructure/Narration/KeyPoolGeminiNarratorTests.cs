using System.Net;
using System.Text;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Infrastructure.Narration;

public sealed class KeyPoolGeminiNarratorTests
{
    private const string K0 = "fake-key-zero";
    private const string K1 = "fake-key-one";
    private const string K2 = "fake-key-two";

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ListLogger : ILogger<GeminiNarratorService>, ILogger<KeyPoolGeminiNarrator>
    {
        public List<string> Messages { get; } = [];
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            Messages.Add(text);
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(text);
            }
        }
    }

    // Mirrors Google's 429 body: RetryInfo.retryDelay plus QuotaFailure.violations[].quotaId.
    private static HttpResponseMessage Quota429(string retryDelay, string quotaId) =>
        new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"details\":["
                + "{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaId\":\"" + quotaId + "\"}]},"
                + "{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"" + retryDelay + "\"}]}}",
                Encoding.UTF8, "application/json"),
        };

    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> KeysSeen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.GetValues("x-goog-api-key").Single();
            KeysSeen.Add(key);
            return Task.FromResult(respond(key));
        }
    }

    private static HttpResponseMessage Ok(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) + "}]}}]}",
                Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Status(HttpStatusCode code, int? retryAfterSeconds = null)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent("{}") };
        if (retryAfterSeconds is { } s)
        {
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
        }

        return r;
    }

    private static (KeyPoolGeminiNarrator Narrator, FakeHandler Handler, FakeClock Clock, ListLogger Log) Create(
        Func<string, HttpResponseMessage> respond, params string[] keys)
    {
        var opts = new NarrationOptions { Enabled = true, TimeoutSeconds = 1, ApiKeys = keys };
        var clock = new FakeClock();
        var log = new ListLogger();
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var gemini = new GeminiNarratorService(http, Options.Create(opts), log);
        var pool = new KeyPool(Options.Create(opts), clock);
        return (new KeyPoolGeminiNarrator(gemini, pool, log), handler, clock, log);
    }

    private static readonly NarrationContext Ctx = new(RoomType.Combat, "Heroes", 3, false) { NextRoom = RoomType.Chest };
    private const string ValidScenario = "A troll blocks the bridge. What do you do?";

    [Fact]
    public async Task RoundRobin_UsesKeysInOrderAndWraps()
    {
        var (n, h, _, _) = Create(_ => Ok("Done."), K0, K1, K2);

        for (var i = 0; i < 4; i++)
        {
            await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        }

        h.KeysSeen.Should().Equal(K0, K1, K2, K0);
    }

    [Fact]
    public async Task RateLimited_RotatesToNextKeyAndSucceeds()
    {
        var (n, h, _, _) = Create(k => k == K0 ? Status(HttpStatusCode.TooManyRequests) : Ok(ValidScenario), K0, K1);

        var result = await n.GenerateScenarioAsync(Ctx, CancellationToken.None);

        result.Should().Be(ValidScenario);
        h.KeysSeen.Should().Equal(K0, K1);
    }

    [Fact]
    public async Task RateLimited_Turn_RotatesToNextKeyAndParsesBothParts()
    {
        const string json = "{\"outcome\":\"The bridge holds.\",\"next\":\"A vault gapes. What do you do?\"}";
        var (n, h, _, _) = Create(k => k == K0 ? Status(HttpStatusCode.TooManyRequests) : Ok(json), K0, K1);

        var result = await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        result.Should().Be(new TurnNarration("The bridge holds.", "A vault gapes. What do you do?"));
        h.KeysSeen.Should().Equal(K0, K1);
    }

    [Fact]
    public async Task AllKeysRateLimited_ReturnsFallbackWithoutThrowing()
    {
        var (n, h, _, _) = Create(_ => Status(HttpStatusCode.TooManyRequests), K0, K1, K2);

        var scenario = await n.GenerateScenarioAsync(Ctx, CancellationToken.None);
        var turn = await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        scenario.Should().Be(FallbackFlavor.Scenario(RoomType.Combat, 3));
        turn.Outcome.Should().Be(FallbackFlavor.Outcome(RoomType.Combat, true, 3));
        turn.Next.Should().Be(FallbackFlavor.Scenario(RoomType.Chest, 4));
        h.KeysSeen.Should().HaveCount(3, "second call finds every key cooling and makes no request");
    }

    [Fact]
    public async Task CooledKey_IsSkippedThenReusedAfterCooldown()
    {
        var limited = true;
        var (n, h, clock, _) = Create(k => k == K0 && limited ? Status(HttpStatusCode.TooManyRequests, 30) : Ok("Fine."), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None); // K0 429 -> K1
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None); // K0 cooling -> K1
        h.KeysSeen.Should().Equal(K0, K1, K1);

        limited = false;
        clock.Now += TimeSpan.FromSeconds(31);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        h.KeysSeen.Should().Contain(K0);
    }

    [Fact]
    public async Task DefaultCooldown_IsSixtySecondsWithoutRetryAfter()
    {
        var (n, h, clock, _) = Create(k => k == K0 ? Status(HttpStatusCode.TooManyRequests) : Ok("Fine."), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        clock.Now += TimeSpan.FromSeconds(59);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().NotContain(K0);

        clock.Now += TimeSpan.FromSeconds(2);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().Contain(K0);
    }

    [Fact]
    public async Task NonRateLimitError_DoesNotRotate_ReturnsFallback()
    {
        var (n, h, _, _) = Create(_ => Status(HttpStatusCode.InternalServerError), K0, K1);

        var outcome = await n.NarrateTurnAsync(false, Ctx, CancellationToken.None);

        outcome.Outcome.Should().Be(FallbackFlavor.Outcome(RoomType.Combat, false, 3));
        h.KeysSeen.Should().Equal(K0);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var (n, _, _, _) = Create(_ => Ok("x"), K0);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => n.NarrateTurnAsync(true, Ctx, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Logs_NeverContainKeyValues_ButMayContainIndex()
    {
        var (n, _, _, log) = Create(k => k == K0 ? Status(HttpStatusCode.TooManyRequests) : Status(HttpStatusCode.InternalServerError), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        log.Messages.Should().NotBeEmpty();
        log.Messages.Should().NotContain(m => m.Contains(K0) || m.Contains(K1));
    }

    // ---- 429 body: retryDelay, quota type, log noise ----

    [Fact]
    public async Task BodyRetryDelay_IsUsedAsCooldown()
    {
        var (n, h, clock, _) = Create(k => k == K0 ? Quota429("120s", "GenerateRequestsPerMinutePerProjectPerModel-FreeTier") : Ok("Fine."), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None); // K0 429 -> K1
        clock.Now += TimeSpan.FromSeconds(100);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().NotContain(K0, "retryDelay of 120s has not elapsed (default would be 60s)");

        clock.Now += TimeSpan.FromSeconds(21);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().Contain(K0);
    }

    [Fact]
    public async Task DailyQuota_CoolsKeyForAtLeastOneHour_EvenWithShortRetryDelay()
    {
        var (n, h, clock, _) = Create(k => k == K0 ? Quota429("20s", "GenerateRequestsPerDayPerProjectPerModel-FreeTier") : Ok("Fine."), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        clock.Now += TimeSpan.FromMinutes(59);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().NotContain(K0);

        clock.Now += TimeSpan.FromMinutes(2);
        h.KeysSeen.Clear();
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        h.KeysSeen.Should().Contain(K0);
    }

    [Fact]
    public async Task RateLimitLog_IncludesQuotaId_ButNeverKeyValues()
    {
        var (n, _, _, log) = Create(_ => Quota429("10s", "GenerateRequestsPerDayPerProjectPerModel-FreeTier"), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        log.Messages.Should().Contain(m => m.Contains("GenerateRequestsPerDayPerProjectPerModel-FreeTier"));
        log.Messages.Should().NotContain(m => m.Contains(K0) || m.Contains(K1));
    }

    [Fact]
    public async Task Exhaustion_WarnsOnce_NotOnEveryFallbackCall()
    {
        var (n, h, _, log) = Create(_ => Status(HttpStatusCode.TooManyRequests), K0, K1);

        await n.NarrateTurnAsync(true, Ctx, CancellationToken.None); // both keys 429
        log.Warnings.Clear();
        for (var i = 0; i < 5; i++)
        {
            await n.NarrateTurnAsync(true, Ctx, CancellationToken.None);
        }

        h.KeysSeen.Should().HaveCount(2, "all later calls skip HTTP while every key is cooling");
        log.Warnings.Should().BeEmpty("the exhaustion warning was already emitted once");
    }

    // ---- Key resolution ----

    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create(p.Key, p.Value))).Build();

    [Fact]
    public void Resolve_IndexedKeys_ScanUntilMissing_AndDropBlankAndDuplicates()
    {
        var cfg = Config(("GoogleFlash:ApiKey_0", "a"), ("GoogleFlash:ApiKey_1", " "), ("GoogleFlash:ApiKey_2", "a"),
            ("GoogleFlash:ApiKey_3", "d"));

        // Index 1 is blank but present, so scanning continues; duplicate "a" dropped.
        NarrationKeyResolver.Resolve(cfg).Should().Equal("a", "d");
    }

    [Fact]
    public void Resolve_ScanStopsAtFirstMissingIndex()
    {
        var cfg = Config(("GoogleFlash:ApiKey_0", "a"), ("GoogleFlash:ApiKey_2", "c"));

        NarrationKeyResolver.Resolve(cfg).Should().Equal("a");
    }

    [Fact]
    public void Resolve_Precedence_IndexedThenArrayThenEnvThenLegacy()
    {
        var all = Config(("GoogleFlash:ApiKey_0", "idx"), ("Gemini:ApiKeys:0", "arr"),
            ("GEMINI_KEY_1", "env"), ("GoogleFlash:ApiKey", "legacy"));
        NarrationKeyResolver.Resolve(all).Should().Equal("idx");

        var noIdx = Config(("Gemini:ApiKeys:0", "arr"), ("Gemini:ApiKeys:1", "arr2"), ("GEMINI_KEY_1", "env"), ("GoogleFlash:ApiKey", "legacy"));
        NarrationKeyResolver.Resolve(noIdx).Should().Equal("arr", "arr2");

        var envOnly = Config(("GEMINI_KEY_1", "env1"), ("GEMINI_KEY_2", "env2"), ("GoogleFlash:ApiKey", "legacy"));
        NarrationKeyResolver.Resolve(envOnly).Should().Equal("env1", "env2");

        var legacy = Config(("GoogleFlash:ApiKey", "legacy"));
        NarrationKeyResolver.Resolve(legacy).Should().Equal("legacy");

        NarrationKeyResolver.Resolve(Config()).Should().BeEmpty();
    }
}
