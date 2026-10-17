using System.Net;
using System.Net.Http.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public class CastConfrontationVoteErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Operation = "Command:CastConfrontationVote";

    private (WebApplicationFactory<Program> Factory, HttpClient Client) Build(int roll)
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new ScriptedRoller(roll));
            s.AddSingleton<IPartyNotifier>(new ChaosRecordingNotifier());
        }));
        return (f, f.CreateClient());
    }

    private static Task<HttpResponseMessage> VoteAsync(HttpClient client, Guid id, string player, string vote = "forgive") =>
        client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = player, vote });

    [Fact]
    [CoversError(ErrorCodes.Vote.NoConfrontation)]
    public async Task WithoutAConfrontation_Returns409Problem()
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);

        using var problem = await ProblemBody.ReadAsync(await VoteAsync(client, id, "Merlin"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("vote.no_confrontation");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    [CoversError(ErrorCodes.Vote.NotEligible)]
    public async Task TheAccused_Returns400Problem_WithTheMessageUnderPlayerName()
    {
        var (f, client) = Build(30);
        using var _ = f;
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);
        ChaosTestSupport.CachedParty(f, id).DropVault(20, null);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" }))
            .EnsureSuccessStatusCode();

        using var problem = await ProblemBody.ReadAsync(await VoteAsync(client, id, "Conan"), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("vote.not_eligible");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("playerName").Should().Be("Conan");
        problem.Root.GetProperty("errors").GetProperty("playerName").EnumerateArray().Single().GetString()
            .Should().Be("That player cannot vote in this confrontation.");
    }

    [Fact]
    public async Task UnknownParty_Returns404Problem()
    {
        var (f, client) = Build(0);
        using var _ = f;

        using var problem = await ProblemBody.ReadAsync(await VoteAsync(client, Guid.NewGuid(), "Merlin"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("party.not_found");
        problem.Operation.Should().Be(Operation);
    }

    [Fact]
    public async Task MissingVote_Returns400ValidationProblem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/parties/{Guid.NewGuid()}/confrontation/votes", new { playerName = "Merlin" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("validation.invalid_request");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").TryGetProperty("vote", out _).Should().BeTrue();
    }
}
