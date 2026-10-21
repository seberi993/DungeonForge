using DungeonForge.Infrastructure.Narration;
using System.Net;
using System.Text;
using System.Text.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Narration;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Infrastructure.Narration;

// The narrator never throws at its caller, so its failures are only visible as coded events. Keys and response bodies must
// never reach a log line.
public sealed class NarrationEventTests
{
    private const string Key0 = "AIza-secret-key-0";
    private static readonly NarrationContext Ctx = new(RoomType.Combat, "Heroes", 3, false) { NextRoom = RoomType.Chest };

    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request.Headers.GetValues("x-goog-api-key").Single()));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused for https://example.test/?key=" + Key0);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string text) =>
        Json(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) + "}]}}]}");

    private static HttpResponseMessage Quota429() =>
        Json(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\","
            + "\"violations\":[{\"quotaId\":\"GenerateRequestsPerMinute\"}]}]}}");

    private static (KeyPoolGeminiNarrator Narrator, GeminiNarratorService Gemini, LogCapture Logs) Create(
        HttpMessageHandler handler)
    {
        var logs = new LogCapture();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        var opts = new NarrationOptions { Enabled = true, TimeoutSeconds = 1, ApiKey = Key0, ApiKeys = [Key0] };
        var http = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var gemini = new GeminiNarratorService(http, Options.Create(opts), loggers.CreateLogger<GeminiNarratorService>());
        var pool = new KeyPool(Options.Create(opts), TimeProvider.System);
        return (new KeyPoolGeminiNarrator(gemini, pool, loggers.CreateLogger<KeyPoolGeminiNarrator>()), gemini, logs);
    }

    private static LogEntry Only(LogCapture logs, int eventId) =>
        logs.Entries.Should().ContainSingle(e => e.EventId.Id == eventId).Subject;

    [Fact]
    public async Task NonSuccessStatus_IsLoggedWithTheStatusOnly()
    {
        var (_, gemini, logs) = Create(new FakeHandler(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"SECRET-BODY\"}")));

        await gemini.GenerateScenarioAsync(Ctx, CancellationToken.None);

        var entry = Only(logs, LogEvents.NarratorHttpFailedId);
        entry.Message.Should().Contain("500").And.NotContain("SECRET-BODY");
    }

    [Fact]
    public async Task RateLimit_IsLoggedWithTheQuota_AndEachKeyCooldownIsCoded()
    {
        var (narrator, _, logs) = Create(new FakeHandler(_ => Quota429()));

        await narrator.GenerateScenarioAsync(Ctx, CancellationToken.None);

        Only(logs, LogEvents.NarratorRateLimitedId).Message.Should().Contain("GenerateRequestsPerMinute");
        Only(logs, LogEvents.KeyRateLimitedId).Message.Should().Contain("#0");
        Only(logs, LogEvents.AllKeysRateLimitedId).Level.Should().Be(LogLevel.Warning);

        await narrator.GenerateScenarioAsync(Ctx, CancellationToken.None);

        Only(logs, LogEvents.AllKeysRateLimitedId);
        Only(logs, LogEvents.NoKeyAvailableId).Level.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task MalformedTurnJson_IsLogged()
    {
        var (_, gemini, logs) = Create(new FakeHandler(_ => Text("this is not the json we asked for")));

        await gemini.NarrateTurnAsync(true, Ctx, CancellationToken.None);

        Only(logs, LogEvents.NarratorResponseMalformedId);
    }

    [Fact]
    public async Task EmptyResponseText_IsLogged()
    {
        var (_, gemini, logs) = Create(new FakeHandler(_ => Text("   ")));

        await gemini.GenerateScenarioAsync(Ctx, CancellationToken.None);

        Only(logs, LogEvents.NarratorEmptyResponseId);
    }

    [Fact]
    public async Task NetworkFailure_LogsTheExceptionTypeOnly_NeverTheMessageOrKey()
    {
        var (_, gemini, logs) = Create(new ThrowingHandler());

        await gemini.GenerateScenarioAsync(Ctx, CancellationToken.None);

        var entry = Only(logs, LogEvents.NarratorCallFailedId);
        entry.Message.Should().Contain("HttpRequestException");
        entry.Message.Should().NotContain(Key0).And.NotContain("connection refused");
        entry.Exception.Should().BeNull("the exception text can carry a URL or key");
    }
}
