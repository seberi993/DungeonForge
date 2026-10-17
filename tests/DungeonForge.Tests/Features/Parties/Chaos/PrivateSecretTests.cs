using DungeonForge.Api.Infrastructure.Narration;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class ChaosDispatcherSecretTests
{
    private static ChaosDispatcher Dispatcher(ChaosRecordingNotifier n) =>
        new(n, new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);

    [Fact]
    public async Task DispatchRoomSecrets_SendsOnlyPrivateMessagesToTheIntendedPlayers_NeverPublic()
    {
        var notifier = new ChaosRecordingNotifier();
        var party = ChaosTestSupport.NewParty(ChaosTestSupport.ChaosOn);

        await Dispatcher(notifier).DispatchRoomSecretsAsync(party, RoomType.Chest, 1);

        notifier.Privates.Select(p => p.Player).Should().BeEquivalentTo("Merlin", "Lyra", "Dru");
        notifier.Privates.Should().OnlyContain(p => p.Message.Kind == PrivateKinds.Secret && p.PartyId == party.Id);
        notifier.PublicTraffic.Should().BeEmpty();
        notifier.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchRoomSecrets_DoesNothing_WhenChaosIsOff()
    {
        var notifier = new ChaosRecordingNotifier();

        await Dispatcher(notifier).DispatchRoomSecretsAsync(ChaosTestSupport.NewParty(), RoomType.Chest, 1);

        notifier.Privates.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchWhispers_RoutesByNameOrClass_AndDropsUnknownTargets()
    {
        var notifier = new ChaosRecordingNotifier();
        var party = ChaosTestSupport.NewParty();

        await Dispatcher(notifier).DispatchWhispersAsync(party,
            [new("Mage", "by class"), new("lyra", "by name"), new("Nobody", "lost")]);

        notifier.Privates.Select(p => (p.Player, p.Message.Text)).Should().Equal(("Merlin", "by class"), ("Lyra", "by name"));
        notifier.Privates.Should().OnlyContain(p => p.Message.Kind == PrivateKinds.Whisper);
        notifier.PublicTraffic.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_SwallowsNotifierFailures()
    {
        var party = ChaosTestSupport.NewParty(ChaosTestSupport.ChaosOn);
        var d = new ChaosDispatcher(new ThrowingPrivateNotifier(), new ScriptedRoller(0), NullLogger<ChaosDispatcher>.Instance);

        var act = () => d.DispatchRoomSecretsAsync(party, RoomType.Chest, 1);

        await act.Should().NotThrowAsync();
    }

    private sealed class ThrowingPrivateNotifier : IPartyNotifier
    {
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) => Task.CompletedTask;
        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) => Task.CompletedTask;
        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) => Task.CompletedTask;
        public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendPrivateAsync(Guid partyId, string playerName, PrivateMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}

public class PrivateDeliveryHubTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<(Guid Id, Microsoft.AspNetCore.SignalR.Client.HubConnection Merlin, Microsoft.AspNetCore.SignalR.Client.HubConnection Lyra, List<PrivateMessage> MerlinGot, List<PrivateMessage> LyraGot, List<PrivateMessage> BystanderGot, Microsoft.AspNetCore.SignalR.Client.HubConnection Bystander)>
        SetUpAsync(WebApplicationFactory<Program> f)
    {
        var id = await ChaosTestSupport.CreateAsync(f.CreateClient(), ChaosTestSupport.ChaosOn, ChaosTestSupport.FourMembers);
        var merlin = ChaosTestSupport.Connect(f);
        var lyra = ChaosTestSupport.Connect(f);
        var bystander = ChaosTestSupport.Connect(f);
        List<PrivateMessage> m = [], l = [], b = [];
        merlin.On<PrivateMessage>("PrivateMessage", x => { lock (m) m.Add(x); });
        lyra.On<PrivateMessage>("PrivateMessage", x => { lock (l) l.Add(x); });
        bystander.On<PrivateMessage>("PrivateMessage", x => { lock (b) b.Add(x); });
        await merlin.StartAsync();
        await lyra.StartAsync();
        await bystander.StartAsync();
        await merlin.InvokeAsync("RegisterPlayer", id, "Merlin");
        await lyra.InvokeAsync("RegisterPlayer", id, "Lyra");
        await bystander.InvokeAsync("JoinParty", id);
        return (id, merlin, lyra, m, l, b, bystander);
    }

    [Fact]
    public async Task Secrets_ReachOnlyTheTargetsConnection()
    {
        var (id, merlin, lyra, merlinGot, lyraGot, bystanderGot, bystander) = await SetUpAsync(factory);
        await using var _1 = merlin;
        await using var _2 = lyra;
        await using var _3 = bystander;
        var party = ChaosTestSupport.CachedParty(factory, id);

        await factory.Services.GetRequiredService<ChaosDispatcher>().DispatchRoomSecretsAsync(party, RoomType.Chest, 1);
        await Task.Delay(500);

        merlinGot.Should().ContainSingle().Which.Kind.Should().Be(PrivateKinds.Secret);
        lyraGot.Should().ContainSingle().Which.Kind.Should().Be(PrivateKinds.Secret);
        merlinGot[0].Text.Should().NotBe(lyraGot[0].Text);
        bystanderGot.Should().BeEmpty();
    }

    [Fact]
    public async Task Whisper_ReachesOnlyItsTarget()
    {
        var (id, merlin, lyra, merlinGot, lyraGot, bystanderGot, bystander) = await SetUpAsync(factory);
        await using var _1 = merlin;
        await using var _2 = lyra;
        await using var _3 = bystander;
        var party = ChaosTestSupport.CachedParty(factory, id);

        await factory.Services.GetRequiredService<ChaosDispatcher>()
            .DispatchWhispersAsync(party, [new Whisper("Mage", "The Bard took 10 extra gold while you were looking away")]);
        await Task.Delay(500);

        merlinGot.Should().ContainSingle().Which.Text.Should().Contain("Bard");
        lyraGot.Should().BeEmpty();
        bystanderGot.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterPlayer_RejectsUnknownPartyOrPlayer()
    {
        await using var conn = ChaosTestSupport.Connect(factory);
        await conn.StartAsync();
        var id = await ChaosTestSupport.CreateAsync(factory.CreateClient(), ChaosTestSupport.ChaosOn);

        var noParty = () => conn.InvokeAsync("RegisterPlayer", Guid.NewGuid(), "Conan");
        var noPlayer = () => conn.InvokeAsync("RegisterPlayer", id, "Ghost");

        await noParty.Should().ThrowAsync<HubException>();
        await noPlayer.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task DisconnectedPlayers_ReceiveNothingAndNothingThrows()
    {
        var (id, merlin, lyra, merlinGot, _, _, bystander) = await SetUpAsync(factory);
        await using var _2 = lyra;
        await using var _3 = bystander;
        await merlin.StopAsync();
        await merlin.DisposeAsync();
        var party = ChaosTestSupport.CachedParty(factory, id);

        var act = () => factory.Services.GetRequiredService<ChaosDispatcher>().DispatchRoomSecretsAsync(party, RoomType.Chest, 1);

        await act.Should().NotThrowAsync();
        merlinGot.Should().BeEmpty();
    }
}
