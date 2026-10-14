using System.Net;
using System.Text;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DungeonForge.Tests.Infrastructure.Narration;

public class TurnAggregationNarrationTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"The glyphs flare.\"}]}}]}", Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task NarrateOutcome_WithManyActions_UsesOneHttpCallContainingAllInputs()
    {
        var opts = new NarrationOptions { ApiKey = "k", Enabled = true };
        var handler = new CountingHandler();
        var svc = new GeminiNarratorService(
            new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) }, Options.Create(opts), NullLogger<GeminiNarratorService>.Instance);
        var ctx = new NarrationContext(RoomType.Puzzle, "Heroes", 2, false)
        {
            Actions = [new(Guid.NewGuid(), "Conan", "smash it"), new(Guid.NewGuid(), "Merlin", "read the runes")],
        };

        var text = await svc.NarrateOutcomeAsync("Pull the lever", true, ctx, CancellationToken.None);

        text.Should().Be("The glyphs flare.");
        handler.Calls.Should().Be(1);
        using var doc = JsonDocument.Parse(handler.Body!);
        var user = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        user.Should().Contain("Conan: smash it").And.Contain("Merlin: read the runes");
    }

    [Fact]
    public void Fallback_Puzzle_HasChoicesAndOutcomes()
    {
        var choices = FallbackFlavor.Choices(RoomType.Puzzle, 1);
        choices.Options.Should().HaveCount(3).And.OnlyContain(o => !string.IsNullOrWhiteSpace(o));
        choices.Prompt.Should().NotBeNullOrWhiteSpace();
        FallbackFlavor.Outcome(RoomType.Puzzle, true, 0).Should().NotBeNullOrWhiteSpace();
        FallbackFlavor.Outcome(RoomType.Puzzle, false, 0).Should().NotBe(FallbackFlavor.Outcome(RoomType.Puzzle, true, 0));
        FallbackFlavor.Outcome(RoomType.Puzzle, true, 0).Should().NotBe(FallbackFlavor.Outcome(RoomType.Chest, true, 0));
    }
}
