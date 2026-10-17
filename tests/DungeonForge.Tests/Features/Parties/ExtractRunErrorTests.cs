using System.Net;
using System.Net.Http.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public class ExtractRunErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Operation = "Command:ExtractRun";

    [Fact]
    [CoversError(ErrorCodes.Extract.NoPact)]
    public async Task WithoutATraitorPact_Returns409Problem()
    {
        var client = factory.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/extract", new { playerName = "Conan" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.Conflict);
        problem.Code.Should().Be("extract.no_pact");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    [CoversError(ErrorCodes.Party.PlayerUnknown)]
    public async Task UnknownPlayer_Returns400Problem_WithTheMessageUnderPlayerName()
    {
        var client = factory.CreateClient();
        var id = await PartyTestBuilders.CreateAsync(client, new PartyModifiers(TraitorPact: true));

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/extract", new { playerName = "Ghost" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("party.player_unknown");
        problem.Parameter("playerName").Should().Be("Ghost");
        problem.Root.GetProperty("errors").GetProperty("playerName").EnumerateArray().Single().GetString()
            .Should().Be("No active player with that name.");
    }

    [Fact]
    public async Task UnknownParty_Returns404Problem()
    {
        var id = Guid.NewGuid();

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/parties/{id}/extract", new { playerName = "Conan" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.NotFound);
        problem.Code.Should().Be("party.not_found");
        problem.Operation.Should().Be(Operation);
    }

    [Fact]
    public async Task BlankName_Returns400ValidationProblem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/extract", new { playerName = "" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("validation.invalid_request");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").TryGetProperty("playerName", out _).Should().BeTrue();
    }
}
