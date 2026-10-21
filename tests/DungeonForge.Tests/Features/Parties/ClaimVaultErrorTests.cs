using System.Net;
using System.Net.Http.Json;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public class ClaimVaultErrorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Operation = "Command:ClaimVault";

    // A roll of 30 makes a pocket claim "caught", which opens a confrontation against Conan.
    private (WebApplicationFactory<Program> Factory, HttpClient Client) Build(int roll)
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new ScriptedRoller(roll));
            s.AddSingleton<IPartyNotifier>(new ChaosRecordingNotifier());
        }));
        return (f, f.CreateClient());
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid id, string player, string choice = "share") =>
        client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = player, choice });

    [Fact]
    [CoversError(ErrorCodes.Vault.NoVault)]
    public async Task NoVault_Returns409Problem()
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        using var problem = await ProblemBody.ReadAsync(await ClaimAsync(client, id, "Conan"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("vault.no_vault");
        problem.Operation.Should().Be(Operation);
        problem.Parameter("partyId").Should().Be(id.ToString());
    }

    [Fact]
    [CoversError(ErrorCodes.Vault.ConfrontationActive)]
    public async Task WhileAConfrontationIsOpen_Returns409Problem()
    {
        var (f, client) = Build(30);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);
        PartyTestBuilders.CachedParty(f, id).DropVault(20, null);
        (await ClaimAsync(client, id, "Conan", "pocket")).EnsureSuccessStatusCode();

        using var problem = await ProblemBody.ReadAsync(await ClaimAsync(client, id, "Merlin"), HttpStatusCode.Conflict);

        problem.Code.Should().Be("vault.confrontation_active");
        problem.Operation.Should().Be(Operation);
    }

    [Fact]
    public async Task UnknownPlayer_Returns400Problem_WithTheMessageUnderPlayerName()
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);
        PartyTestBuilders.CachedParty(f, id).DropVault(5, null);

        using var problem = await ProblemBody.ReadAsync(await ClaimAsync(client, id, "Ghost"), HttpStatusCode.BadRequest);

        problem.Code.Should().Be("party.player_unknown");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").GetProperty("playerName").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task UnknownParty_Returns404Problem()
    {
        var (f, client) = Build(0);
        using var _ = f;

        using var problem = await ProblemBody.ReadAsync(await ClaimAsync(client, Guid.NewGuid(), "Conan"), HttpStatusCode.NotFound);

        problem.Code.Should().Be("party.not_found");
        problem.Operation.Should().Be(Operation);
    }

    [Fact]
    public async Task MissingChoice_Returns400ValidationProblem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/vault/claim", new { playerName = "Conan" });

        using var problem = await ProblemBody.ReadAsync(response, HttpStatusCode.BadRequest);
        problem.Code.Should().Be("validation.invalid_request");
        problem.Operation.Should().Be(Operation);
        problem.Root.GetProperty("errors").TryGetProperty("choice", out _).Should().BeTrue();
    }
}
