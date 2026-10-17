using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Features.Scores;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Tests.Features.Parties;

// A broken hub or leaderboard must never fail the request, and every swallowed failure leaves one coded event naming the
// operation and the party.
public class BroadcastFailureLoggingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (WebApplicationFactory<Program> Scope, HttpClient Client, LogCapture Logs) Host(
        IPartyNotifier notifier, IRoller? roller = null)
    {
        var logs = new LogCapture();
        var scope = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddLogging(l => l.AddProvider(logs));
            s.AddSingleton(notifier);
            if (roller is not null)
            {
                s.AddSingleton(roller);
            }
        }));
        return (scope, scope.CreateClient(), logs);
    }

    private static void AssertOneBroadcastFailure(LogCapture logs, string operation, Guid partyId)
    {
        var failures = logs.Entries.Where(e => e.EventId.Id == LogEvents.BroadcastFailedId).ToList();
        failures.Should().ContainSingle();
        failures[0].Level.Should().Be(LogLevel.Warning);
        failures[0].Scope.Should().Contain("Operation", operation).And.Contain("PartyId", partyId);
    }

    [Fact]
    public async Task JoinParty_HubDown_StillJoins_AndLogsTheFailure()
    {
        var (scope, client, logs) = Host(new FailingNotifier());
        using var _ = scope;
        var created = await PartyTestData.PostPartyAsync(client);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("partyId").GetGuid();
        var code = doc.RootElement.GetProperty("inviteCode").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = "Zed", @class = "bard" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertOneBroadcastFailure(logs, "Command:JoinParty", id);
    }

    [Fact]
    public async Task UnlockTurn_HubDown_StillUnlocks_AndLogsTheFailure()
    {
        var (scope, client, logs) = Host(new FailingNotifier(), new FixedRoller(0));
        using var _ = scope;
        var id = await PartyTestData.CreatePartyAsync(client);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ExploreTestSupport.SubmitAsync(client, id, ExploreTestSupport.WholeParty()[0])).EnsureSuccessStatusCode();
        logs.Entries.Clear();

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/unlock", new { memberId = ExploreTestSupport.ConanId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertOneBroadcastFailure(logs, "Command:UnlockTurn", id);
    }

    [Fact]
    public async Task ClaimVault_HubDown_StillClaims_AndLogsEachFailedSend()
    {
        var (scope, client, logs) = Host(new FailingNotifier(failChaos: true), new ScriptedRoller(0));
        using var _ = scope;
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);
        ChaosTestSupport.CachedParty(scope, id).DropVault(20, null);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "share" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var failures = logs.Entries.Where(e => e.EventId.Id == LogEvents.BroadcastFailedId).ToList();
        failures.Should().HaveCount(2, "the public chaos event and the party update both failed");
        failures.Should().Contain(e => (string?)e.Scope["Operation"] == "Command:ClaimVault");
        failures.Should().Contain(e => (string?)e.Scope["Operation"] == ChaosOperations.Send);
    }

    [Fact]
    public async Task CastVote_HubDown_StillResolves_AndLogsThePartyUpdateFailure()
    {
        var (scope, client, logs) = Host(new FailingNotifier(), new ScriptedRoller(30));
        using var _ = scope;
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);
        ChaosTestSupport.CachedParty(scope, id).DropVault(20, null);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" }))
            .EnsureSuccessStatusCode();
        await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Merlin", vote = "demandCut" });
        await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Lyra", vote = "demandCut" });
        logs.Entries.Clear();

        var last = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Dru", vote = "forgive" });

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Entries.Should().Contain(e =>
            e.EventId.Id == LogEvents.BroadcastFailedId && (string?)e.Scope["Operation"] == "Command:CastConfrontationVote");
    }

    private sealed class DeadBoard : IScoreBoard
    {
        public Task RecordAsync(RunScore score, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RunScore>> TopAsync(int limit, CancellationToken ct = default) =>
            throw new HttpRequestException("leaderboard is down");
    }

    [Fact]
    public async Task HighScores_BoardDown_ReturnsAnEmptyBoard_AndLogsTheFailure()
    {
        var logs = new LogCapture();
        using var scope = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddLogging(l => l.AddProvider(logs));
            s.AddSingleton<IScoreBoard>(new DeadBoard());
        }));

        var response = await scope.CreateClient().GetAsync("/api/scores");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var failure = logs.Entries.Should().ContainSingle(e => e.EventId.Id == LogEvents.ScoreBoardReadFailedId).Subject;
        failure.Level.Should().Be(LogLevel.Warning);
        failure.Scope.Should().ContainKey("Operation").WhoseValue.Should().Be("Query:GetHighScores");
    }
}
