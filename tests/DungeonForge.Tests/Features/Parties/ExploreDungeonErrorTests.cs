using System.Net;
using System.Net.Http.Json;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Features.Parties.ExploreTestSupport;

namespace DungeonForge.Tests.Features.Parties;

// Error bodies of POST /explore. The three operations mirror the three things the one route does.
public sealed class ExploreDungeonErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string GetScenario = "Query:GetScenario";
    private const string LockIn = "Command:LockIn";
    private const string ResolveTurn = "Command:ResolveTurn";

    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private (WebApplicationFactory<Program> Scope, HttpClient Client) Host(IRoller roller, bool clientNarration = false)
    {
        var scope = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton(roller);
            s.AddSingleton<IPartyNotifier>(new ChaosRecordingNotifier());
            s.Configure<NarrationOptions>(o =>
            {
                o.ClientNarration = clientNarration;
                o.ClientDeadlineSeconds = 30;
            });
        }));
        _derived.Add(scope);
        return (scope, scope.CreateClient());
    }

    private static Task<HttpResponseMessage> SubmitOnlyAsync(HttpClient client, Guid id) =>
        client.PostAsJsonAsync($"/api/parties/{id}/explore", new { actions = WholeParty(), submitOnly = true });

    [Fact]
    [CoversError(ErrorCodes.Turn.PartyDefeated)]
    public async Task DefeatedParty_Returns409Problem_ForEachOfTheThreeOperations()
    {
        var (_, client) = Host(new FixedRoller(0, Fail));
        var id = await PartyTestData.CreatePartyAsync(client, members: [new { name = "Merlin", @class = "mage" }]);
        for (var i = 0; i < 3; i++)
        {
            using var _ = await TurnAsync(client, id);
        }

        using var prompt = await ProblemBody.ReadAsync(await PromptAsync(client, id), HttpStatusCode.Conflict);
        using var resolve = await ProblemBody.ReadAsync(await ResolveAsync(client, id), HttpStatusCode.Conflict);
        using var lockIn = await ProblemBody.ReadAsync(await SubmitOnlyAsync(client, id), HttpStatusCode.Conflict);

        prompt.Code.Should().Be("turn.party_defeated");
        prompt.Operation.Should().Be(GetScenario);
        resolve.Operation.Should().Be(ResolveTurn);
        lockIn.Operation.Should().Be(LockIn);
        prompt.Parameter("partyId").Should().Be(id.ToString());
        prompt.Parameter("turn").Should().Be("3");
    }

    [Fact]
    public async Task UnknownParty_Returns404Problem_ForEachOfTheThreeOperations()
    {
        var (_, client) = Host(new FixedRoller(0));
        var id = Guid.NewGuid();

        using var prompt = await ProblemBody.ReadAsync(await PromptAsync(client, id), HttpStatusCode.NotFound);
        using var resolve = await ProblemBody.ReadAsync(await ResolveAsync(client, id), HttpStatusCode.NotFound);
        using var lockIn = await ProblemBody.ReadAsync(await SubmitOnlyAsync(client, id), HttpStatusCode.NotFound);

        prompt.Code.Should().Be("party.not_found");
        prompt.Operation.Should().Be(GetScenario);
        resolve.Operation.Should().Be(ResolveTurn);
        lockIn.Operation.Should().Be(LockIn);
    }

    [Fact]
    [CoversError(ErrorCodes.Turn.ConfrontationInProgress)]
    public async Task WhileAConfrontationIsOpen_Returns400Problem_WithTheMessageUnderActions()
    {
        var (scope, client) = Host(new ScriptedRoller(30));
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);
        ChaosTestSupport.CachedParty(scope, id).DropVault(20, null);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" }))
            .EnsureSuccessStatusCode();

        using var problem = await ProblemBody.ReadAsync(
            await ResolveAsync(client, id, Act("go", "Conan", ConanId)), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("turn.confrontation_in_progress");
        problem.Operation.Should().Be(ResolveTurn);
        problem.Parameter("partyId").Should().Be(id.ToString());
        problem.Root.GetProperty("errors").GetProperty("actions").EnumerateArray().Single().GetString()
            .Should().Be("A confrontation is in progress; settle the vote before the party moves on.");
    }

    [Fact]
    [CoversError(ErrorCodes.Turn.NoActiveEncounter)]
    public async Task ResolvingBeforeAnyScenario_Returns400Problem()
    {
        var (_, client) = Host(new FixedRoller(0));
        var id = await PartyTestData.CreatePartyAsync(client);

        using var problem = await ProblemBody.ReadAsync(await ResolveAsync(client, id), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("turn.no_active_encounter");
        problem.Operation.Should().Be(ResolveTurn);
        problem.Root.GetProperty("errors").GetProperty("actions").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    [CoversError(ErrorCodes.Turn.ScenarioPending)]
    public async Task ResolvingWhileTheScenarioIsBeingWritten_Returns400Problem()
    {
        var (_, client) = Host(new FixedRoller(0), clientNarration: true);
        var id = await PartyTestData.CreatePartyAsync(client);
        using var first = await ReadAsync(await PromptAsync(client, id));
        first.RootElement.GetProperty("scenarioPending").GetBoolean().Should().BeTrue();

        using var problem = await ProblemBody.ReadAsync(await ResolveAsync(client, id), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("turn.scenario_pending");
        problem.Operation.Should().Be(ResolveTurn);
    }

    [Theory]
    [InlineData(false, ResolveTurn)]
    [InlineData(true, LockIn)]
    public async Task ActionsOverTheLimits_Return400ValidationProblem_NamingTheOperationThatWasAsked(bool submitOnly, string operation)
    {
        var (_, client) = Host(new FixedRoller(0));
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/parties/{id}/explore", new { actions = new[] { Act(new string('x', 201)) }, submitOnly });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("validation.invalid_request");
        problem.Operation.Should().Be(operation);
        problem.Root.GetProperty("errors").GetProperty("actions").EnumerateArray().Should().ContainSingle();
    }
}
