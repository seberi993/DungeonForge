using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests.Features.Parties.Chaos;

public class BlindActionPhaseTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "ZZZ-private-plan";

    private (WebApplicationFactory<Program> F, ChaosRecordingNotifier Notifier, CapturingNarrator Narrator) Build(int success = ExploreTestSupport.Succeed)
    {
        var notifier = new ChaosRecordingNotifier();
        var narrator = new CapturingNarrator();
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(0, success));
            s.AddSingleton<IPartyNotifier>(notifier);
            s.AddSingleton<IDungeonNarrator>(narrator);
        }));
        return (f, notifier, narrator);
    }

    private static PlayerAction Act(string name, Guid id, string text, CovertIntent? covert = null) => new(id, name, text, covert);

    [Fact]
    public async Task Locking_BroadcastsOnlyPlayerIsReady_NeverActionTextOrDiscussion()
    {
        var (f, notifier, _) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ExploreTestSupport.SubmitAsync(client, id,
            Act("Conan", ExploreTestSupport.ConanId, Secret, new CovertIntent(CovertKind.Backstab, "Merlin")));
        response.EnsureSuccessStatusCode();

        notifier.DiscussionBroadcasts.Should().Be(0);
        notifier.Events.Should().ContainSingle(e => e.Kind == ChaosEvent.PlayerReady).Which.Text.Should().Be("Conan is ready");
        notifier.PublicTraffic.Should().NotContain(Secret).And.NotContainEquivalentOf("backstab");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(Secret);
    }

    [Fact]
    public async Task PromptDuringLockIn_DoesNotExposeOthersActions()
    {
        var (f, _, _) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();
        (await ExploreTestSupport.SubmitAsync(client, id, Act("Conan", ExploreTestSupport.ConanId, Secret))).EnsureSuccessStatusCode();

        var again = await ExploreTestSupport.PromptAsync(client, id);

        (await again.Content.ReadAsStringAsync()).Should().NotContain(Secret);
    }

    [Fact]
    public async Task NonChaosParty_AlsoKeepsLockedAnswersPrivate()
    {
        var (f, notifier, _) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await PartyTestData.CreatePartyAsync(client);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        (await ExploreTestSupport.SubmitAsync(client, id, Act("Conan", ExploreTestSupport.ConanId, Secret))).EnsureSuccessStatusCode();

        notifier.DiscussionBroadcasts.Should().Be(0);
        notifier.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task MasterTurn_RevealsBackstabInCombatLogOnlyAfterResolve_AndFeedsTheNarrator()
    {
        var (f, notifier, narrator) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        (await ExploreTestSupport.SubmitAsync(client, id,
            Act("Mercy", ExploreTestSupport.MercyId, "I steady the line", new CovertIntent(CovertKind.Backstab, "Conan")))).EnsureSuccessStatusCode();
        (await ExploreTestSupport.SubmitAsync(client, id, Act("Merlin", ExploreTestSupport.MerlinId, "I weave a ward"))).EnsureSuccessStatusCode();
        notifier.Turns.Should().BeEmpty();
        notifier.PublicTraffic.Should().NotContainEquivalentOf("backstab");

        var resolved = await ExploreTestSupport.ResolveAsync(client, id,
            Act("Conan", ExploreTestSupport.ConanId, "We press forward carefully"));
        resolved.EnsureSuccessStatusCode();

        var turn = notifier.Turns.Should().ContainSingle().Subject;
        turn.CombatLog.Should().ContainSingle().Which.Should().Contain("Mercy").And.Contain("Conan").And.Contain("backstab");
        turn.Hits.Should().Contain(h => h.Name == "Conan" && h.Damage == CovertResolver.BackstabDamage);
        await Task.Delay(200);
        SpinWait.SpinUntil(() => narrator.Turns.Count == 1, TimeSpan.FromSeconds(10)).Should().BeTrue();
        narrator.Turns[0].Treachery.Should().ContainSingle().Which.Should().Contain("backstab");
        narrator.Turns[0].Motifs.Should().NotBeEmpty();
        narrator.Turns[0].Roster.Select(r => r.Name).Should().Contain(["Conan", "Merlin", "Mercy"]);
    }

    [Fact]
    public async Task Sabotage_ForcesTheTargetsOvertActionToFail()
    {
        var (f, notifier, _) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ExploreTestSupport.ResolveAsync(client, id,
            Act("Conan", ExploreTestSupport.ConanId, "We press forward carefully"),
            Act("Merlin", ExploreTestSupport.MerlinId, "I weave a ward", new CovertIntent(CovertKind.Sabotage, "Conan")),
            Act("Mercy", ExploreTestSupport.MercyId, "I steady the line"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var hits = doc.RootElement.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("name").GetString()).ToArray();
        hits.Should().Contain("Conan");
        notifier.Turns.Single().CombatLog.Should().ContainSingle().Which.Should().Contain("sabotage");
    }

    [Fact]
    public async Task InvalidCovertTarget_IsRejectedBeforeLocking()
    {
        var (f, _, _) = Build();
        using var _ = f;
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        var response = await ExploreTestSupport.SubmitAsync(client, id,
            Act("Conan", ExploreTestSupport.ConanId, "go", new CovertIntent(CovertKind.Backstab, new string('x', 200))));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChaosParty_PutsGoldInTheUnclaimedVault_NotThePartyPurse()
    {
        var notifier = new ChaosRecordingNotifier();
        using var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IRoller>(new FixedRoller(1)); // chest rooms pay gold
            s.AddSingleton<IPartyNotifier>(notifier);
        }));
        var client = f.CreateClient();
        var id = await ChaosTestSupport.CreateAsync(client, ChaosTestSupport.ChaosOn);
        (await ExploreTestSupport.PromptAsync(client, id)).EnsureSuccessStatusCode();

        (await ExploreTestSupport.ResolveAsync(client, id)).EnsureSuccessStatusCode();

        var party = ChaosTestSupport.CachedParty(f, id);
        party.Gold.Should().Be(0);
        party.Vault.Should().NotBeNull();
        party.Vault!.Gold.Should().BeGreaterThan(0);
        notifier.Events.Should().Contain(e => e.Kind == ChaosEvent.Vault);
    }
}
