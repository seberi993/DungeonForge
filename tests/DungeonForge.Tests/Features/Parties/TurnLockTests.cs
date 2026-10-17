using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static DungeonForge.Tests.Support.PartyTestBuilders;
using DungeonForge.Tests.Support;

namespace DungeonForge.Tests.Features.Parties;

public sealed class TurnLockTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private sealed class CountingNarrator : IDungeonNarrator
    {
        private int _turnCalls;
        public int TurnCalls => Volatile.Read(ref _turnCalls);
        public NarrationContext? TurnContext { get; private set; }

        public Task<string> GenerateScenarioAsync(NarrationContext ctx, CancellationToken ct) =>
            Task.FromResult("A door looms. What do you do?");

        public Task<TurnNarration> NarrateTurnAsync(bool success, NarrationContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref _turnCalls);
            TurnContext = ctx;
            return Task.FromResult(new TurnNarration("You prevail.", "Another door. What do you do?"));
        }
    }

    private readonly List<WebApplicationFactory<Program>> _derived = [];

    public void Dispose() => _derived.ForEach(f => f.Dispose());

    private async Task<(HttpClient Client, Guid Id, CountingNarrator Narrator, CapturingNotifier Notifier)> ArrangeAsync()
    {
        var narrator = new CountingNarrator();
        var notifier = new CapturingNotifier();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(0, Succeed));
            s.AddSingleton<IDungeonNarrator>(narrator);
            s.AddSingleton<IPartyNotifier>(notifier);
        }));
        _derived.Add(derived);
        var client = derived.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await PromptAsync(client, id)).EnsureSuccessStatusCode();
        return (client, id, narrator, notifier);
    }

    private static async Task<int> TurnOfAsync(HttpClient client, Guid id)
    {
        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        return state.RootElement.GetProperty("turn").GetInt32();
    }

    private static IEnumerable<(string Name, bool LockedIn)> Locks(JsonElement e) =>
        e.GetProperty("lockStatus").EnumerateArray().Select(l => (l.GetProperty("name").GetString()!, l.GetProperty("lockedIn").GetBoolean()));

    [Fact]
    public async Task OneOfThreeLocked_DoesNotAdvanceTurn_OrCallNarrator()
    {
        var (client, id, narrator, _) = await ArrangeAsync();

        var response = await SubmitAsync(client, id, WholeParty()[0]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("narrationPending").GetBoolean().Should().BeFalse();
        Locks(doc.RootElement).Should().Equal(("Conan", true), ("Merlin", false), ("Mercy", false));
        (await TurnOfAsync(client, id)).Should().Be(0);
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task TwoOfThreeLocked_DoesNotAdvanceTurn_OrCallNarrator()
    {
        var (client, id, narrator, _) = await ArrangeAsync();
        var party = WholeParty();

        (await SubmitAsync(client, id, party[0])).EnsureSuccessStatusCode();
        var response = await SubmitAsync(client, id, party[1]);

        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        Locks(doc.RootElement).Should().Equal(("Conan", true), ("Merlin", true), ("Mercy", false));
        (await TurnOfAsync(client, id)).Should().Be(0);
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task ThreeOfThreeLocked_ResolvesExactlyOnce_WithEveryMembersTextInTheMasterPrompt()
    {
        var (client, id, narrator, notifier) = await ArrangeAsync();
        var party = WholeParty();
        (await SubmitAsync(client, id, party[0])).EnsureSuccessStatusCode();
        (await SubmitAsync(client, id, party[1])).EnsureSuccessStatusCode();

        var last = await SubmitAsync(client, id, party[2]);

        last.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(last);
        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("narrationPending").GetBoolean().Should().BeTrue();
        await notifier.NarrationAsync;
        narrator.TurnCalls.Should().Be(1);
        var prompt = MasterPromptBuilder.Build(true, narrator.TurnContext!);
        prompt.Should().Contain("We press forward carefully").And.Contain("I weave a ward").And.Contain("I steady the line");
        (await TurnOfAsync(client, id)).Should().Be(1);
    }

    [Fact]
    public async Task ReLock_ReplacesText_WithoutResolving()
    {
        var (client, id, narrator, _) = await ArrangeAsync();
        var conan = WholeParty()[0];
        (await SubmitAsync(client, id, conan)).EnsureSuccessStatusCode();

        using var doc = await ReadAsync(await SubmitAsync(client, id, conan with { Text = "actually, retreat" }));

        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        doc.RootElement.ToString().Should().NotContain("actually, retreat");
        Locks(doc.RootElement).Count(l => l.LockedIn).Should().Be(1);
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateLockBySameMember_DoesNotCountTwice()
    {
        var (client, id, narrator, _) = await ArrangeAsync();
        var party = WholeParty();

        (await SubmitAsync(client, id, party[0])).EnsureSuccessStatusCode();
        (await SubmitAsync(client, id, party[0])).EnsureSuccessStatusCode();
        using var doc = await ReadAsync(await SubmitAsync(client, id, party[0], party[1]));

        doc.RootElement.GetProperty("turn").GetInt32().Should().Be(0);
        narrator.TurnCalls.Should().Be(0);
        (await TurnOfAsync(client, id)).Should().Be(0);
    }

    [Fact]
    public async Task DirectResolve_WithMissingLocks_Returns409Waiting_AndKeepsTurn()
    {
        var (client, id, narrator, _) = await ArrangeAsync();

        var response = await ResolveAsync(client, id, WholeParty()[0]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var doc = await ReadAsync(response);
        doc.RootElement.GetProperty("status").GetString().Should().Be("waiting");
        doc.RootElement.GetProperty("locked").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("required").GetInt32().Should().Be(3);
        Locks(doc.RootElement).Should().Equal(("Conan", true), ("Merlin", false), ("Mercy", false));
        (await TurnOfAsync(client, id)).Should().Be(0);
        narrator.TurnCalls.Should().Be(0);
    }

    [Fact]
    public async Task DirectResolve_AfterOthersLocked_ResolvesWithTheFinalLock()
    {
        var (client, id, narrator, notifier) = await ArrangeAsync();
        var party = WholeParty();
        (await SubmitAsync(client, id, party[0], party[1])).EnsureSuccessStatusCode();

        var response = await ResolveAsync(client, id, party[2]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await notifier.NarrationAsync;
        narrator.TurnCalls.Should().Be(1);
    }

    [Fact]
    public async Task EachLock_BroadcastsTurnLockUpdated_AndResolveBroadcastsClearedState()
    {
        var (client, id, _, notifier) = await ArrangeAsync();

        foreach (var member in WholeParty())
        {
            (await SubmitAsync(client, id, member)).EnsureSuccessStatusCode();
        }

        await notifier.NarrationAsync;
        lock (notifier.LockUpdates)
        {
            notifier.LockUpdates.Should().OnlyContain(u => u.PartyId == id);
            notifier.LockUpdates.Select(u => u.Status.Count(s => s.LockedIn)).Should().Equal(1, 2, 0);
        }
    }

    [Fact]
    public async Task ConcurrentLastTwoLocks_ResolveExactlyOnce()
    {
        var (client, id, narrator, notifier) = await ArrangeAsync();
        var party = WholeParty();
        (await SubmitAsync(client, id, party[0])).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(SubmitAsync(client, id, party[1]), SubmitAsync(client, id, party[2]));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        await notifier.NarrationAsync;
        narrator.TurnCalls.Should().Be(1);
        (await TurnOfAsync(client, id)).Should().Be(1);
    }

    [Fact]
    public async Task LockState_ClearsAfterResolve()
    {
        var (client, id, _, notifier) = await ArrangeAsync();
        (await ResolveAsync(client, id)).EnsureSuccessStatusCode();
        await notifier.NarrationAsync;

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));
        Locks(state.RootElement).Should().Equal(("Conan", false), ("Merlin", false), ("Mercy", false));
        using var next = await PromptWhenReadyAsync(client, id);
        Locks(next.RootElement).Should().OnlyContain(l => !l.LockedIn);
    }

    [Fact]
    public async Task GetPartyState_ExposesLockStatusPerMember()
    {
        var (client, id, _, _) = await ArrangeAsync();
        (await SubmitAsync(client, id, WholeParty()[1])).EnsureSuccessStatusCode();

        using var state = await ReadAsync(await client.GetAsync($"/api/parties/{id}"));

        Locks(state.RootElement).Should().Equal(("Conan", false), ("Merlin", true), ("Mercy", false));
    }

    [Fact]
    public async Task Unlock_ReleasesTheLock_AndBroadcasts()
    {
        var (client, id, _, notifier) = await ArrangeAsync();
        (await SubmitAsync(client, id, WholeParty()[0])).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/parties/{id}/unlock", new { memberId = ConanId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await ReadAsync(response);
        Locks(doc.RootElement).Should().OnlyContain(l => !l.LockedIn);
        lock (notifier.LockUpdates)
        {
            notifier.LockUpdates.Last().Status.Should().OnlyContain(s => !s.LockedIn);
        }
    }

    [Fact]
    public async Task Unlock_UnknownParty_Returns404()
    {
        var response = await factory.CreateClient().PostAsJsonAsync($"/api/parties/{Guid.NewGuid()}/unlock", new { memberId = ConanId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
