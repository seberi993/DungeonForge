using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Social;

public class DebriefAndTelemetryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static void EndRun(SocialHarness h, Guid id)
    {
        var party = h.Party(id);
        party.ApplyMemberOutcomes([.. party.Members.Select(m => new DamageResult(m.Name, 99))], 0);
        party.IsPartyDefeated.Should().BeTrue();
    }

    private static async Task<HttpResponseMessage> DebriefAsync(SocialHarness h, Guid id, string name) =>
        await h.Client.GetAsync($"/api/parties/{id}/social/debrief?playerName={name}");

    [Fact]
    [CoversError(ErrorCodes.Social.RunInProgress)]
    public async Task TheDungeonKeepsItsSecrets_UntilTheRunIsOver()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        await h.Client.PostAsJsonAsync($"/api/parties/{id}/social/preferences", new { playerName = "Conan", debriefOptIn = true });

        using var problem = await ProblemBody.ReadAsync(await DebriefAsync(h, id, "Conan"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("social.run_in_progress");
        problem.Operation.Should().Be("Query:GetDebrief");
    }

    [Fact]
    public async Task AnOptedInPlayer_SeesTheirExactProfile_AbilitiesAndTheMomentsThatInvolvedThem()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        await h.SeedAllAsync(id, SocialHarness.Four);
        await h.Client.PostAsJsonAsync($"/api/parties/{id}/social/preferences", new { playerName = "Conan", debriefOptIn = true });
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Merlin", ["Conan"], "Did you take the gold?", "accusation"));
        (await h.ReplyAsync(id, ask, "Conan", "honest", "No")).EnsureSuccessStatusCode();
        EndRun(h, id);

        var response = await DebriefAsync(h, id, "Conan");

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("optedIn").GetBoolean().Should().BeTrue();
        root.GetProperty("axes").GetProperty("valor").GetInt32().Should().BePositive("Conan answered like a fighter");
        root.GetProperty("axes").EnumerateObject().Select(p => p.Name).Should().Contain(["valor", "cunning", "empathy", "discipline", "chaos", "greed", "lore", "resolve"]);
        root.GetProperty("abilities").GetArrayLength().Should().Be(2);
        root.GetProperty("abilities")[0].GetProperty("budget").GetInt32().Should().BeInRange(14, 24);
        root.GetProperty("credibility").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("moments").EnumerateArray().Should().Contain(m => m.GetProperty("rule").GetString() == "R-THEFT");
        root.GetProperty("run").GetProperty("seeds").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task APlayerWhoDidNotOptIn_GetsOnlyFlavorAndTheRunCounters()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        await h.SeedAllAsync(id, SocialHarness.Four);
        EndRun(h, id);

        var response = await DebriefAsync(h, id, "Conan");

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("optedIn").GetBoolean().Should().BeFalse();
        root.TryGetProperty("axes", out _).Should().BeFalse();
        root.TryGetProperty("abilities", out _).Should().BeFalse();
        root.TryGetProperty("credibility", out _).Should().BeFalse();
        root.GetProperty("tags").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("run").GetProperty("seeds").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task TheDebrief_NeverShowsAnotherPlayersExactNumbers()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        await h.SeedAllAsync(id, SocialHarness.Four);
        await h.Client.PostAsJsonAsync($"/api/parties/{id}/social/preferences", new { playerName = "Conan", debriefOptIn = true });
        EndRun(h, id);

        var other = await DebriefAsync(h, id, "Merlin");

        (await other.Content.ReadAsStringAsync()).Should().NotContain("budget").And.NotContain("\"axes\"");
    }

    [Fact]
    public async Task TheCountersFollowTheRun()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        await h.SeedAllAsync(id, SocialHarness.Four);
        (await h.SeedAsync(id, "Conan", "Another answer entirely")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Merlin", ["Conan"], "Where were you?"));
        (await h.ReplyAsync(id, ask, "Conan", "dodge")).EnsureSuccessStatusCode();
        (await h.AskAsync(id, "Merlin", ["Lyra"], "Where were you?")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var counters = h.Party(id).Social().Counters;

        counters["seeds"].Should().Be(4);
        counters["asks_sent"].Should().Be(1);
        counters["asks_replied"].Should().Be(1);
        counters["rate_limited"].Should().Be(1);
    }

    [Fact]
    public async Task AbuseIsCounted_LowEffortCopiedAndDuplicateWhispers()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await h.SeedAsync(id, "Conan", "aaaaaaaaaa")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await h.SeedAsync(id, "Conan", "I fight at the door and protect")).EnsureSuccessStatusCode();
        (await h.SeedAsync(id, "Merlin", "I fight at the door and protect")).EnsureSuccessStatusCode();
        (await h.AskAsync(id, "Conan", ["Lyra"], "Who has the key?")).EnsureSuccessStatusCode();
        h.Clock.Advance(TimeSpan.FromSeconds(100));
        (await h.AskAsync(id, "Conan", ["Dru"], "who has the KEY")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        h.Party(id).Social().Counters["abuse_flags"].Should().Be(3);
    }

    [Fact]
    public async Task SupervisorDecisionsAreCountedByVerdictAndEventType()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        var ask = await SocialHarness.AskIdAsync(await h.AskAsync(id, "Merlin", ["Conan", "Lyra"], "Do we trust Dru?"));
        (await h.ReplyAsync(id, ask, "Conan", "honest", "No")).EnsureSuccessStatusCode();
        (await h.ReplyAsync(id, ask, "Lyra", "honest", "No")).EnsureSuccessStatusCode();

        var counters = h.Party(id).Social().Counters;

        counters["event:ask_reply"].Should().Be(2);
        counters["event:ask_consensus"].Should().Be(1);
        counters["verdict:celebrate"].Should().Be(1);
    }
}
