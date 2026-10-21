using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Application.Errors;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Parties;

public class JoinPartyErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Operation = "Command:JoinParty";

    private static async Task<(Guid Id, string Code)> CreateAsync(HttpClient client, object[]? members = null)
    {
        var created = await PartyTestData.PostPartyAsync(client, members: members);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("partyId").GetGuid(), doc.RootElement.GetProperty("inviteCode").GetString()!);
    }

    [Fact]
    [CoversError(ErrorCodes.Party.InviteNotFound)]
    public async Task UnknownCode_Returns404Problem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/parties/by-code/ZZZZ-ZZZZ/join", new { name = "Zed", @class = "bard" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.NotFound);
        problem.Code.Should().Be("party.invite_not_found");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("code").Should().Be("ZZZZ-ZZZZ");
    }

    [Fact]
    [CoversError(ErrorCodes.Party.NameTaken)]
    public async Task DuplicateName_Returns409Problem()
    {
        var client = factory.CreateClient();
        var (id, code) = await CreateAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = "conan", @class = "bard" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.Conflict);
        problem.Code.Should().Be("party.name_taken");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
        problem.Parameter("playerName").Should().Be("conan");
    }

    [Fact]
    [CoversError(ErrorCodes.Party.Full)]
    public async Task FullParty_Returns409Problem()
    {
        var client = factory.CreateClient();
        object[] eight = [.. Enumerable.Range(1, 8).Select(i => new { name = $"Hero{i}", @class = "warrior" })];
        var (id, code) = await CreateAsync(client, eight);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = "Latecomer", @class = "bard" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.Conflict);
        problem.Code.Should().Be("party.full");
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    [CoversError(ErrorCodes.Validation.InvalidRequest)]
    public async Task InvalidBody_Returns400Problem_KeepingTheErrorsDictionary()
    {
        var client = factory.CreateClient();
        var (_, code) = await CreateAsync(client);

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = "", @class = "bard" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("validation.invalid_request");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").GetProperty("name").EnumerateArray().Should().ContainSingle();
    }
}
