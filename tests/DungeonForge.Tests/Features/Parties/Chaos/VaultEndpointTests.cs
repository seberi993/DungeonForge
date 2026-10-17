using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class VaultEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (WebApplicationFactory<Program> F, ChaosRecordingNotifier N) Build(int roll)
    {
        var notifier = new ChaosRecordingNotifier();
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new ScriptedRoller(roll));
            s.AddSingleton<IPartyNotifier>(notifier);
        }));
        return (f, notifier);
    }

    private static async Task<Guid> ChaosPartyWithVaultAsync(WebApplicationFactory<Program> f, int gold = 20)
    {
        var id = await PartyTestBuilders.CreateAsync(f.CreateClient(), PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);
        PartyTestBuilders.CachedParty(f, id).DropVault(gold, null);
        return id;
    }

    [Fact]
    public async Task Share_AnnouncesPubliclyAndPaysTheParty()
    {
        var (f, n) = Build(0);
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosPartyWithVaultAsync(f);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "share" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("shared");
        PartyTestBuilders.CachedParty(f, id).Gold.Should().Be(20);
        n.Events.Should().Contain(e => e.Kind == ChaosEvent.VaultClaimed && e.Text.Contains("Conan"));
    }

    [Fact]
    public async Task PocketUnseen_AnnouncesNothingAboutTheClaimant_AndConfirmsPrivately()
    {
        var (f, n) = Build(99);
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosPartyWithVaultAsync(f);

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" });

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("pocketed");
        n.PublicTraffic.Should().NotContain("Conan");
        n.Events.Should().ContainSingle(e => e.Kind == ChaosEvent.VaultClaimed).Which.Text.Should().NotContain("Conan");
        n.Privates.Should().ContainSingle(p => p.Player == "Conan");
        PartyTestBuilders.CachedParty(f, id).PocketedGold("Conan").Should().Be(20);
    }

    [Fact]
    public async Task PocketCaught_StartsAPublicConfrontation_AndVotesResolveIt()
    {
        var (f, n) = Build(30);
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosPartyWithVaultAsync(f);

        var claim = await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" });
        using var claimDoc = JsonDocument.Parse(await claim.Content.ReadAsStringAsync());
        claimDoc.RootElement.GetProperty("outcome").GetString().Should().Be("caught");
        n.Events.Should().Contain(e => e.Kind == ChaosEvent.Confrontation && e.Text.Contains("Conan"));

        var v1 = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Merlin", vote = "demandCut" });
        v1.StatusCode.Should().Be(HttpStatusCode.OK);
        n.PublicTraffic.Should().NotContain("demandCut");
        await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Lyra", vote = "demandCut" });
        var last = await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Dru", vote = "forgive" });

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        using var lastDoc = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        lastDoc.RootElement.GetProperty("resolved").GetBoolean().Should().BeTrue();
        lastDoc.RootElement.GetProperty("outcome").GetString().Should().Be("demandCut");
        PartyTestBuilders.CachedParty(f, id).Gold.Should().Be(10);
        n.Events.Should().Contain(e => e.Kind == ChaosEvent.ConfrontationResolved);
    }

    [Fact]
    public async Task ClaimEndpoint_ValidatesAndMapsStatuses()
    {
        var (f, _) = Build(0);
        using var _ = f;
        var client = f.CreateClient();
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        (await client.PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/vault/claim", new { playerName = "Conan", choice = "share" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "", choice = "share" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "bogus" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "share" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        PartyTestBuilders.CachedParty(f, id).DropVault(5, null);
        (await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Ghost", choice = "share" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VoteEndpoint_ValidatesAndMapsStatuses()
    {
        var (f, _) = Build(0);
        using var _ = f;
        var client = f.CreateClient();
        var id = await PartyTestBuilders.CreateAsync(client, PartyTestBuilders.ChaosOn, PartyTestBuilders.FourMembers);

        (await client.PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/confrontation/votes", new { playerName = "Merlin", vote = "forgive" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Merlin", vote = "nope" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/parties/{id}/confrontation/votes", new { playerName = "Merlin", vote = "forgive" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ExploreIsBlocked_WhileAConfrontationIsOpen()
    {
        var (f, _) = Build(30);
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosPartyWithVaultAsync(f);
        (await PartyTestBuilders.PromptAsync(client, id)).EnsureSuccessStatusCode();
        await client.PostAsJsonAsync($"/api/parties/{id}/vault/claim", new { playerName = "Conan", choice = "pocket" });

        var response = await PartyTestBuilders.SubmitAsync(client, id, PartyTestBuilders.Act("go", "Conan", PartyTestBuilders.ConanId));

        PartyTestBuilders.CachedParty(f, id).Confrontation.Should().NotBeNull();
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
    }
}
