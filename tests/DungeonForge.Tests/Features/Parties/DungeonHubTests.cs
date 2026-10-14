using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties;

public sealed class DungeonHubTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class ThrowingNotifier : IPartyNotifier
    {
        public Task PartyUpdatedAsync(GetPartyStateResponse state, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task TurnResolvedAsync(ExploreResponse turn, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task NarrationReadyAsync(NarrationReady narration, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task DiscussionUpdatedAsync(Guid partyId, IReadOnlyList<PlayerAction> actions, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task TurnLockUpdatedAsync(Guid partyId, IReadOnlyList<LockStatusDto> status, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    private WebApplicationFactory<Program> WithFixedRoller() =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IRoller>(new FixedRoller(0, ExploreTestSupport.Fail))));

    private static async Task<HttpResponseMessage> ExploreTurnAsync(HttpClient client, Guid id)
    {
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();
        return await ExploreTestSupport.ResolveAsync(client, id);
    }

    private static HubConnection Connect(WebApplicationFactory<Program> f) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(f.Server.BaseAddress, "/hubs/dungeon"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
            })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            })
            .Build();

    private static Task<Guid> CreatePartyAsync(HttpClient client) => PartyTestData.CreatePartyAsync(client);

    [Fact]
    public async Task Explore_BroadcastsTurnResolvedAndPartyUpdated_ToEveryConnectionInTheParty()
    {
        using var f = WithFixedRoller();
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);

        await using var a = Connect(f);
        await using var b = Connect(f);
        var turnB = new TaskCompletionSource<ExploreResponse>();
        var stateB = new TaskCompletionSource<GetPartyStateResponse>();
        b.On<ExploreResponse>("TurnResolved", t => turnB.TrySetResult(t));
        b.On<GetPartyStateResponse>("PartyUpdated", s => stateB.TrySetResult(s));
        await a.StartAsync();
        await b.StartAsync();
        await a.InvokeAsync("JoinParty", id);
        await b.InvokeAsync("JoinParty", id);

        var response = await ExploreTurnAsync(client, id);
        response.EnsureSuccessStatusCode();

        var timeout = TimeSpan.FromSeconds(10);
        var turn = await turnB.Task.WaitAsync(timeout);
        turn.PartyId.Should().Be(id);
        turn.Turn.Should().Be(1);
        turn.RoomType.Should().Be(RoomType.Combat);
        turn.TargetName.Should().Be("Conan");
        turn.Damage.Should().Be(2);
        turn.IsPartyDefeated.Should().BeFalse();
        turn.Gold.Should().Be(0);
        turn.FlavorEvent.Should().NotBeNullOrWhiteSpace();

        var state = await stateB.Task.WaitAsync(timeout);
        state.PartyId.Should().Be(id);
        state.Name.Should().Be("Heroes");
        state.Turn.Should().Be(1);
        state.IsPartyDefeated.Should().BeFalse();
        state.Members.Should().HaveCount(3);
        state.Members[0].Name.Should().Be("Conan");
        state.Members[0].Class.Should().Be(CharacterClass.Warrior);
        state.Members[0].Hp.Should().Be(28);
        state.Members[0].MaxHp.Should().Be(30);
        state.Members[0].IsDefeated.Should().BeFalse();
    }

    [Fact]
    public async Task Explore_DoesNotNotifyConnectionsOfOtherParties()
    {
        using var f = WithFixedRoller();
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);
        var otherId = await CreatePartyAsync(client);

        await using var same = Connect(f);
        await using var other = Connect(f);
        var sameGot = new TaskCompletionSource<ExploreResponse>();
        var otherCount = 0;
        same.On<ExploreResponse>("TurnResolved", t => sameGot.TrySetResult(t));
        other.On<ExploreResponse>("TurnResolved", _ => Interlocked.Increment(ref otherCount));
        other.On<GetPartyStateResponse>("PartyUpdated", _ => Interlocked.Increment(ref otherCount));
        await same.StartAsync();
        await other.StartAsync();
        await same.InvokeAsync("JoinParty", id);
        await other.InvokeAsync("JoinParty", otherId);

        (await ExploreTurnAsync(client, id)).EnsureSuccessStatusCode();

        await sameGot.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(300);
        otherCount.Should().Be(0);
    }

    [Fact]
    public async Task JoinParty_WithUnknownParty_Fails()
    {
        await using var conn = Connect(factory);
        await conn.StartAsync();

        var act = () => conn.InvokeAsync("JoinParty", Guid.NewGuid());

        await act.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task LeaveParty_StopsDelivery()
    {
        using var f = WithFixedRoller();
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);
        await using var conn = Connect(f);
        var count = 0;
        conn.On<ExploreResponse>("TurnResolved", _ => Interlocked.Increment(ref count));
        await conn.StartAsync();
        await conn.InvokeAsync("JoinParty", id);
        await conn.InvokeAsync("LeaveParty", id);

        (await ExploreTurnAsync(client, id)).EnsureSuccessStatusCode();

        await Task.Delay(300);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Explore_StillSucceeds_WhenNotifierThrows()
    {
        using var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(0, ExploreTestSupport.Fail));
            s.AddSingleton<IPartyNotifier>(new ThrowingNotifier());
        }));
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);

        var response = await ExploreTurnAsync(client, id);

        response.IsSuccessStatusCode.Should().BeTrue();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Lock_BroadcastsTurnLockUpdated_ToEveryConnectionInTheParty()
    {
        using var f = WithFixedRoller();
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        await using var a = Connect(f);
        await using var b = Connect(f);
        var got = new TaskCompletionSource<(Guid, List<LockStatusDto>)>();
        b.On<Guid, List<LockStatusDto>>("TurnLockUpdated", (pid, s) => got.TrySetResult((pid, s)));
        await a.StartAsync();
        await b.StartAsync();
        await a.InvokeAsync("JoinParty", id);
        await b.InvokeAsync("JoinParty", id);

        (await ExploreTestSupport.SubmitAsync(client, id, ExploreTestSupport.WholeParty()[0])).EnsureSuccessStatusCode();

        var (partyId, status) = await got.Task.WaitAsync(TimeSpan.FromSeconds(10));
        partyId.Should().Be(id);
        status.Select(s => (s.Name, s.LockedIn)).Should().Equal(("Conan", true), ("Merlin", false), ("Mercy", false));
    }

    [Fact]
    public async Task Lock_DoesNotNotifyConnectionsOfOtherParties()
    {
        using var f = WithFixedRoller();
        var client = f.CreateClient();
        var id = await CreatePartyAsync(client);
        var otherId = await CreatePartyAsync(client);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();
        await using var other = Connect(f);
        var count = 0;
        other.On<Guid, List<LockStatusDto>>("TurnLockUpdated", (_, _) => Interlocked.Increment(ref count));
        await other.StartAsync();
        await other.InvokeAsync("JoinParty", otherId);

        (await ExploreTestSupport.SubmitAsync(client, id, ExploreTestSupport.WholeParty()[0])).EnsureSuccessStatusCode();

        await Task.Delay(300);
        count.Should().Be(0);
    }
}
