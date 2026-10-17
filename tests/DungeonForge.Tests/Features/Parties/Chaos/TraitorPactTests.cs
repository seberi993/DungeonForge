using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class PartyTraitorTests
{
    private static Party Pact(params PartyMember[] members) =>
        PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true), members);

    [Fact]
    public void Assignment_PicksOneMemberViaTheRoller_Once()
    {
        var party = Pact();

        party.TryAssignTraitor(new ScriptedRoller(2)).Should().BeTrue();
        party.TraitorName.Should().Be("Lyra");
        party.TryAssignTraitor(new ScriptedRoller(0)).Should().BeFalse();
        party.TraitorName.Should().Be("Lyra");
    }

    [Fact]
    public void Assignment_NeedsThePactAndAtLeastTwoMembers()
    {
        PartyTestBuilders.NewParty().TryAssignTraitor(new ScriptedRoller(0)).Should().BeFalse();
        Pact(new PartyMember("Solo", CharacterClass.Bard)).TryAssignTraitor(new ScriptedRoller(0)).Should().BeFalse();
    }

    [Fact]
    public void TraitorIdentity_IsNeverInTheSnapshotOrPublicState()
    {
        var party = Pact();
        party.TryAssignTraitor(new ScriptedRoller(0));

        var json = System.Text.Json.JsonSerializer.Serialize(party.Snapshot());

        json.Should().NotContainEquivalentOf("traitor");
    }

    [Fact]
    public void WipeBeforeFloorFive_EvaluatesAsATraitorWin()
    {
        var party = Pact();
        party.TryAssignTraitor(new ScriptedRoller(0));
        party.ApplyMemberOutcomes([new("Merlin", 999), new("Lyra", 999), new("Dru", 999)], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.TraitorWinsWipe);
    }

    [Fact]
    public void WipeIsIgnoredWithoutAPact()
    {
        var party = PartyTestBuilders.NewParty();
        party.ApplyMemberOutcomes([.. party.Members.Select(m => new DamageResult(m.Name, 999))], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.None);
    }

    [Fact]
    public void SoloExtraction_RequiresTheIdolAndEveryoneElseGone()
    {
        var party = Pact();
        party.TryAssignTraitor(new ScriptedRoller(0)); // Conan
        party.DropVault(10, "Cursed Idol");
        party.ClaimVault("Conan", VaultChoice.Pocket, new ScriptedRoller(99));
        party.TryExtract("Conan").Should().BeTrue();
        party.EvaluateTraitor().Should().Be(TraitorOutcome.None);

        party.ApplyMemberOutcomes([new("Merlin", 99), new("Lyra", 99), new("Dru", 99)], 0);

        party.EvaluateTraitor().Should().Be(TraitorOutcome.TraitorWinsSoloExtraction);
    }

    [Fact]
    public void Extract_FailsForDefeatedOrUnknownMembers()
    {
        var party = Pact();
        party.ApplyMemberOutcomes([new("Merlin", 99)], 0);

        party.TryExtract("Merlin").Should().BeFalse();
        party.TryExtract("Ghost").Should().BeFalse();
    }
}

public class ChaosDispatcherTraitorTests
{
    [Fact]
    public async Task EnsureTraitor_SendsThePactPrivatelyToExactlyOnePlayer_WhoIsConnected()
    {
        var notifier = new ChaosRecordingNotifier();
        var d = new ChaosDispatcher(notifier, new ScriptedRoller(1), NullLogger<ChaosDispatcher>.Instance);
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));

        await d.EnsureTraitorAsync(party, "Conan");

        notifier.Privates.Should().ContainSingle();
        notifier.Privates[0].Player.Should().Be("Merlin");
        notifier.Privates[0].Message.Kind.Should().Be(PrivateKinds.Traitor);
        notifier.PublicTraffic.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureTraitor_ResendsToTheTraitorWhenTheyReconnect_AndNeverToOthers()
    {
        var notifier = new ChaosRecordingNotifier();
        var d = new ChaosDispatcher(notifier, new ScriptedRoller(1), NullLogger<ChaosDispatcher>.Instance);
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));
        await d.EnsureTraitorAsync(party, "Conan");
        notifier.Privates.Clear();

        await d.EnsureTraitorAsync(party, "Dru");
        notifier.Privates.Should().BeEmpty();
        await d.EnsureTraitorAsync(party, "Merlin");

        notifier.Privates.Should().ContainSingle().Which.Player.Should().Be("Merlin");
    }

    [Fact]
    public async Task AnnounceOutcome_IsPublicAndNamesTheTraitor()
    {
        var notifier = new ChaosRecordingNotifier();
        var d = new ChaosDispatcher(notifier, new ScriptedRoller(1), NullLogger<ChaosDispatcher>.Instance);
        var party = PartyTestBuilders.NewParty(new PartyModifiers(TraitorPact: true));
        party.TryAssignTraitor(new ScriptedRoller(1));

        await d.AnnounceTraitorOutcomeAsync(party, TraitorOutcome.TraitorWinsWipe);

        notifier.Events.Should().ContainSingle(e => e.Kind == ChaosEvent.TraitorOutcome).Which.Text.Should().Contain("Merlin");
    }
}

public class TraitorPactHubTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OnlyTheTraitorsConnection_ReceivesThePact()
    {
        using var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton(sp => new ChaosDispatcher(
                sp.GetRequiredService<IPartyNotifier>(), new ScriptedRoller(1), NullLogger<ChaosDispatcher>.Instance))));
        var id = await PartyTestBuilders.CreateAsync(f.CreateClient(), new PartyModifiers(TraitorPact: true), PartyTestBuilders.FourMembers);
        await using var conan = PartyTestBuilders.Connect(f);
        await using var merlin = PartyTestBuilders.Connect(f);
        List<PrivateMessage> conanGot = [], merlinGot = [];
        conan.On<PrivateMessage>("PrivateMessage", m => { lock (conanGot) conanGot.Add(m); });
        merlin.On<PrivateMessage>("PrivateMessage", m => { lock (merlinGot) merlinGot.Add(m); });
        await conan.StartAsync();
        await merlin.StartAsync();

        await conan.InvokeAsync("RegisterPlayer", id, "Conan");
        await merlin.InvokeAsync("RegisterPlayer", id, "Merlin");
        await Task.Delay(500);

        conanGot.Should().BeEmpty();
        merlinGot.Should().ContainSingle().Which.Kind.Should().Be(PrivateKinds.Traitor);
    }

    [Fact]
    public async Task ExtractEndpoint_ReportsOutcome_OrRejectsWithoutAPact()
    {
        var client = factory.CreateClient();
        var plain = await PartyTestData.CreatePartyAsync(client);
        var pact = await PartyTestBuilders.CreateAsync(client, new PartyModifiers(TraitorPact: true));

        (await client.PostAsJsonAsync($"/api/parties/{plain}/extract", new { playerName = "Conan" }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/extract", new { playerName = "Conan" }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/parties/{pact}/extract", new { playerName = "" }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/parties/{pact}/extract", new { playerName = "Conan" }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
