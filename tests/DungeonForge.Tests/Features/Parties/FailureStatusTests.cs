using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

// Characterization of failure status codes that no other endpoint test pins. Status only: Phase 1 of docs/plans/refactor.md
// keeps these codes and changes the bodies. Row sources are listed in the status table of that plan.
public class FailureStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (WebApplicationFactory<Program> Factory, HttpClient Client) Build(int roll)
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new ScriptedRoller(roll));
            s.AddSingleton<IPartyNotifier>(new ChaosRecordingNotifier());
        }));
        return (f, f.CreateClient());
    }

    // Roll 30 makes the pocket claim "caught", which opens a confrontation against Conan.
    private static async Task<Guid> PartyWithOpenConfrontationAsync(WebApplicationFactory<Program> f, HttpClient client)
    {
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);
        PartyTestBuilders.CachedParty(f, id).DropVault(20, null);
        var claim = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" });
        claim.StatusCode.Should().Be(HttpStatusCode.OK);
        PartyTestBuilders.CachedParty(f, id).Confrontation.Should().NotBeNull();
        return id;
    }

    [Fact]
    public async Task ClaimVault_WhileConfrontationIsOpen_Returns409()
    {
        var (f, client) = Build(30);
        using var _ = f;
        var id = await PartyWithOpenConfrontationAsync(f, client);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Merlin", choice = "share" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ClaimVault_MissingChoice_Returns400()
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("choice");
    }

    [Fact]
    public async Task CastVote_ByTheAccused_Returns400()
    {
        var (f, client) = Build(30);
        using var _ = f;
        var id = await PartyWithOpenConfrontationAsync(f, client);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Conan", vote = "forgive" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("playerName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A name that is definitely longer than forty characters")]
    public async Task CastVote_InvalidPlayerName_Returns400(string playerName)
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName, vote = "forgive" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("playerName");
    }

    [Fact]
    public async Task CastVote_MissingVote_Returns400()
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Merlin" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("vote");
    }

    [Fact]
    public async Task Extract_UnknownPlayer_Returns400()
    {
        var client = factory.CreateClient();
        var pact = await PartyTestBuilders.CreateAsync(client, new PartyModifiers(TraitorPact: true));

        var response = await client.PostAsJsonAsync($"/api/parties/{pact}/extract", new { playerName = "Ghost" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("playerName");
    }

    [Fact]
    public async Task JoinParty_NameTooLong_Returns400()
    {
        var client = factory.CreateClient();
        var created = await PartyTestData.PostPartyAsync(client);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var code = doc.RootElement.GetProperty("inviteCode").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/parties/by-code/{code}/join", new { name = new string('x', 33), @class = "bard" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("name");
    }

    // Pins today's behaviour for an enum value the binder rejects. It never reaches the endpoint filter, so its body is the
    // framework's. Phase 1 step 1.3 decides what to do with it; this only keeps the status stable.
    [Theory]
    [InlineData("vault/claim", "choice")]
    [InlineData("confrontation/votes", "vote")]
    public async Task UnknownEnumValue_Returns400(string route, string field)
    {
        var (f, client) = Build(0);
        using var _ = f;
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);
        var body = new Dictionary<string, string> { ["playerName"] = "Conan", [field] = "bogus" };

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/{route}", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<IReadOnlyCollection<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }
}
