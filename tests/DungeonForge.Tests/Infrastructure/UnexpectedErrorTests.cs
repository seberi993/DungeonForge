using System.Net;
using System.Text;
using System.Text.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Tests.Infrastructure;

public class UnexpectedErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AllowedOrigin = "http://localhost:5200";

    private sealed class ThrowingCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => throw new InvalidOperationException("secret-detail");

        public void Remove(object key)
        {
        }

        public bool TryGetValue(object key, out object? value) => throw new InvalidOperationException("secret-detail");

        public void Dispose()
        {
        }
    }

    private (WebApplicationFactory<Program> Factory, LogCapture Logs) ThrowingHost()
    {
        var logs = new LogCapture();
        var f = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(s => s.AddLogging(l => l.AddProvider(logs)));
            b.ConfigureServices(s => s.AddSingleton<IMemoryCache>(new ThrowingCache()));
        });
        return (f, logs);
    }

    private static Task<HttpResponseMessage> GetPartyAsync(HttpClient client, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/parties/{Guid.NewGuid()}");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request);
    }

    [Fact]
    [CoversError(ErrorCodes.Unexpected.Unhandled)]
    public async Task UnhandledException_Returns500ProblemJson_WithCodeOperationAndTraceId()
    {
        var (f, _) = ThrowingHost();
        using var _ = f;

        var response = await GetPartyAsync(f.CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(500);
        root.GetProperty("code").GetString().Should().Be("unexpected.unhandled");
        root.GetProperty("operation").GetString().Should().Be("Unhandled:GetPartyState");
        root.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("parameters").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task UnhandledException_LeaksNoExceptionDetails()
    {
        var (f, _) = ThrowingHost();
        using var _ = f;

        var body = await (await GetPartyAsync(f.CreateClient())).Content.ReadAsStringAsync();

        body.Should().NotContain("secret-detail").And.NotContain("InvalidOperationException").And.NotContain(" at ");
    }

    [Fact]
    public async Task UnhandledException_IsLoggedOnceAsAnError()
    {
        var (f, logs) = ThrowingHost();
        using var _ = f;

        await GetPartyAsync(f.CreateClient());

        logs.Entries.Where(e => e.Level == LogLevel.Error).Should().ContainSingle();
    }

    [Fact]
    public async Task UnhandledException_KeepsTheCorsHeadersTheBrowserNeeds()
    {
        var (f, _) = ThrowingHost();
        using var _ = f;

        var response = await GetPartyAsync(f.CreateClient(), AllowedOrigin);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be(AllowedOrigin);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"name\": 5}")]
    [InlineData("")]
    [CoversError(ErrorCodes.Request.Malformed)]
    public async Task MalformedBody_Returns400Problem_WithTheMessageUnderErrorsBody(string body)
    {
        var logs = new LogCapture();
        using var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddLogging(l => l.AddProvider(logs))));

        var response = await f.CreateClient().PostAsync("/api/parties", new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("request.malformed");
        doc.RootElement.GetProperty("operation").GetString().Should().Be("BindRequest:CreateParty");
        doc.RootElement.GetProperty("errors").GetProperty("body").EnumerateArray().Should().ContainSingle();
        doc.RootElement.GetRawText().Should().NotContain("BadHttpRequestException");
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error, "a client mistake is not a server error");
    }
}
