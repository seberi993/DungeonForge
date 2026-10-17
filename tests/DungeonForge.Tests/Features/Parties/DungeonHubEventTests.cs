using DungeonForge.Api.Errors;
using DungeonForge.Tests.Errors;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Tests.Features.Parties;

// A hub call the server refuses still throws the same HubException text the client sees; the server also keeps a coded event.
public sealed class DungeonHubEventTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (WebApplicationFactory<Program> Scope, LogCapture Logs) Host()
    {
        var logs = new LogCapture();
        return (factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddLogging(l => l.AddProvider(logs)))), logs);
    }

    [Fact]
    public async Task JoinParty_UnknownParty_KeepsTheClientMessage_AndLogsTheRefusal()
    {
        var (scope, logs) = Host();
        using var _ = scope;
        await using var connection = ChaosTestSupport.Connect(scope);
        await connection.StartAsync();
        var id = Guid.NewGuid();

        var act = () => connection.InvokeAsync("JoinParty", id);

        (await act.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("Party not found.");
        var entry = logs.Entries.Should().ContainSingle(e => e.EventId.Id == LogEvents.HubPartyNotFoundId).Subject;
        entry.Scope.Should().Contain("Operation", "Hub:JoinParty").And.Contain("PartyId", id);
    }

    [Fact]
    public async Task RegisterPlayer_UnknownPlayer_KeepsTheClientMessage_AndLogsTheRefusal()
    {
        var (scope, logs) = Host();
        using var _ = scope;
        var id = await PartyTestData.CreatePartyAsync(scope.CreateClient());
        await using var connection = ChaosTestSupport.Connect(scope);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("RegisterPlayer", id, "Ghost");

        (await act.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("Player not found.");
        var entry = logs.Entries.Should().ContainSingle(e => e.EventId.Id == LogEvents.HubPlayerNotFoundId).Subject;
        entry.Scope.Should().Contain("Operation", "Hub:RegisterPlayer").And.Contain("PartyId", id);
        logs.Entries.Should().NotContain(e => e.Message.Contains("Ghost"), "a typed name is not logged");
    }

    [Fact]
    public async Task RegisterPlayer_UnknownParty_KeepsTheClientMessage_AndLogsTheRefusal()
    {
        var (scope, logs) = Host();
        using var _ = scope;
        await using var connection = ChaosTestSupport.Connect(scope);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("RegisterPlayer", Guid.NewGuid(), "Conan");

        (await act.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("Party not found.");
        logs.Entries.Should().ContainSingle(e => e.EventId.Id == LogEvents.HubPartyNotFoundId)
            .Which.Scope.Should().Contain("Operation", "Hub:RegisterPlayer");
    }
}
