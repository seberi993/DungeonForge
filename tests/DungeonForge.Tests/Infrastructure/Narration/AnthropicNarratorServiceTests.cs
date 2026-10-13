using System.Net;
using System.Text;
using System.Text.Json;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Infrastructure.Narration;

public sealed class AnthropicNarratorServiceTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string TextBody(string text) =>
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":"
        + JsonSerializer.Serialize(text) + "}]}";

    private static (AnthropicNarratorService Svc, FakeHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        Action<NarrationOptions>? configure = null)
    {
        var opts = new NarrationOptions { ApiKey = "test-key", Enabled = true, TimeoutSeconds = 1 };
        configure?.Invoke(opts);
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var svc = new AnthropicNarratorService(http, Options.Create(opts), NullLogger<AnthropicNarratorService>.Instance);
        return (svc, handler);
    }

    private static readonly NarrationContext Ctx = new(RoomType.Combat, "Heroes", 3, true);

    [Fact]
    public async Task Narrate_Success_ReturnsTrimmedFirstContentText()
    {
        var (svc, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, TextBody("  Torches gutter. Shadows lunge.  \n"))));

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().Be("Torches gutter. Shadows lunge.");
    }

    [Fact]
    public async Task Narrate_NotTwoSentences_ReturnsTextAsIs()
    {
        const string text = "One long atmospheric sentence. Two. Three sentences here.";
        var (svc, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, TextBody(text))));

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().Be(text);
    }

    [Fact]
    public async Task Narrate_Request_TargetsMessagesEndpointWithAuthHeaders()
    {
        var (svc, handler) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, TextBody("Ok."))));

        await svc.NarrateAsync(Ctx, CancellationToken.None);

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
        handler.Request.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("test-key");
        handler.Request.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    [Fact]
    public async Task Narrate_RequestBody_ContainsModelAndFactsButNoMath()
    {
        var (svc, handler) = Create(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, TextBody("Ok."))),
            o => o.Model = "claude-test-model");
        var ctx = new NarrationContext(RoomType.Chest, "Silver Wolves", 7, false);

        await svc.NarrateAsync(ctx, CancellationToken.None);

        handler.Body.Should().NotBeNull();
        var body = handler.Body!;
        body.Should().Contain("claude-test-model");
        body.Should().Contain("max_tokens");
        body.Should().Contain("Silver Wolves");
        body.ToLowerInvariant().Should().Contain("chest");
        body.ToLowerInvariant().Should().NotContainAny("\"hp\"", "\"gold\"", "hpchange", "goldfound", "dice");
    }

    [Fact]
    public async Task Narrate_ServerError_ReturnsNonEmptyFallback()
    {
        var (svc, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")));

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Narrate_HttpRequestException_ReturnsNonEmptyFallback()
    {
        var (svc, _) = Create((_, _) => throw new HttpRequestException("boom"));

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Narrate_Timeout_ReturnsNonEmptyFallback()
    {
        var (svc, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json(HttpStatusCode.OK, TextBody("too late"));
        });

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().NotBeNullOrWhiteSpace().And.NotBe("too late");
    }

    [Theory]
    [InlineData("{\"content\":[]}")]
    [InlineData("{\"content\":[{\"type\":\"text\",\"text\":\"   \"}]}")]
    public async Task Narrate_EmptyContent_ReturnsNonEmptyFallback(string json)
    {
        var (svc, _) = Create((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Narrate_MissingApiKey_ReturnsFallbackWithoutCallingApi(string? key)
    {
        var (svc, handler) = Create(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, TextBody("Should not be used."))),
            o => o.ApiKey = key);

        var result = await svc.NarrateAsync(Ctx, CancellationToken.None);

        result.Should().NotBeNullOrWhiteSpace().And.NotBe("Should not be used.");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Narrate_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var (svc, _) = Create(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json(HttpStatusCode.OK, TextBody("never"));
        }, o => o.TimeoutSeconds = 30);

        var act = () => svc.NarrateAsync(Ctx, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
