using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Web.Core;
using DungeonForge.Web.Features.Dungeon;
using DungeonForge.Web.Features.Lobby;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Tests;

public class LocalNarrationTests : TestContext
{
    private static readonly Guid Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string BriefedPrompt =
        """{"partyId":"33333333-3333-3333-3333-333333333333","turn":0,"roomType":"combat","flavorEvent":"","gold":0,"isPartyDefeated":false,"scenarioPending":true,"brief":{"turn":0,"kind":"scenario","system":"SYS","user":"USR","maxTokens":400}}""";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return request.RequestUri.AbsolutePath.EndsWith("/narration", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BriefedPrompt, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeLocalNarrator(LocalNarratorState state, string? reply = null) : ILocalNarrator
    {
        public LocalNarratorState State { get; set; } = state;
        public int Progress { get; set; }
        public string? Status { get; set; }
        public NarrationBrief? Seen { get; private set; }
        public int Enabled { get; private set; }
        public event Action? Changed;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task EnableAsync()
        {
            Enabled++;
            return Task.CompletedTask;
        }

        public Task<string?> GenerateAsync(NarrationBrief brief, CancellationToken ct = default)
        {
            Seen = brief;
            return Task.FromResult(reply);
        }

        public void Raise() => Changed?.Invoke();
    }

    private (IRenderedComponent<DungeonRoom> Cut, RecordingHandler Handler) Render(ILocalNarrator? narrator)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var handler = new RecordingHandler();
        var session = new GameSession();
        session.Start(new PartyResponse(Id, "Heroes", [new("Conan", CharacterClass.Warrior, 30, 30, false)], DateTimeOffset.UtcNow, "K7F2-9QXM"));
        Services.AddSingleton<IDungeonHubClient>(new FakeDungeonHubClient());
        Services.AddSingleton(session);
        Services.AddSingleton(new DungeonApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5062") }));
        if (narrator is not null) Services.AddSingleton(narrator);
        return (RenderComponent<DungeonRoom>(), handler);
    }

    [Fact]
    public void ReadyModel_RunsTheBriefAndSubmitsItsText()
    {
        var narrator = new FakeLocalNarrator(LocalNarratorState.Ready, "A **goblin** waits. What do you do?");
        var (cut, handler) = Render(narrator);

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Path.EndsWith("/narration")));
        narrator.Seen!.System.Should().Be("SYS");
        narrator.Seen.User.Should().Be("USR");
        var body = handler.Requests.Single(r => r.Path.EndsWith("/narration")).Body!;
        body.Should().Contain("\"turn\":0").And.Contain("A **goblin** waits. What do you do?").And.NotContain("declined");
    }

    [Fact]
    public void ModelNotReady_DeclinesSoTheServerNarratesAtOnce()
    {
        var narrator = new FakeLocalNarrator(LocalNarratorState.Downloading);
        var (cut, handler) = Render(narrator);

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Path.EndsWith("/narration")));
        narrator.Seen.Should().BeNull();
        handler.Requests.Single(r => r.Path.EndsWith("/narration")).Body.Should().Contain("\"declined\":true");
    }

    [Fact]
    public void NoLocalNarratorRegistered_StillDeclinesTheBrief()
    {
        var (cut, handler) = Render(null);

        cut.Find("button#explore").Click();

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.Path.EndsWith("/narration")));
        handler.Requests.Single(r => r.Path.EndsWith("/narration")).Body.Should().Contain("\"declined\":true");
    }

    [Fact]
    public void IdleModel_OffersAnExplicitOptIn_ThatEnablesTheDownload()
    {
        var narrator = new FakeLocalNarrator(LocalNarratorState.Idle);
        var (cut, _) = Render(narrator);

        cut.Find("#local-narrator").TextContent.Should().Contain("1 GB").And.Contain("WebGPU");
        cut.Find("button#enable-local-narrator").Click();

        narrator.Enabled.Should().Be(1);
    }

    [Fact]
    public void UnsupportedBrowser_ShowsNoControl()
    {
        var (cut, _) = Render(new FakeLocalNarrator(LocalNarratorState.Unsupported));

        cut.FindAll("#local-narrator").Should().BeEmpty();
    }

    [Fact]
    public void Downloading_ShowsProgress()
    {
        var narrator = new FakeLocalNarrator(LocalNarratorState.Downloading) { Progress = 42, Status = "Fetching weights" };
        var (cut, _) = Render(narrator);

        cut.Find("#local-narrator-progress").GetAttribute("value").Should().Be("42");
        cut.Find("#local-narrator").TextContent.Should().Contain("Fetching weights");
    }
}
