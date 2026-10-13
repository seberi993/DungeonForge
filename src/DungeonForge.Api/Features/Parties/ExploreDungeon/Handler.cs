using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.GetPartyState;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonHandler(
    IMemoryCache cache,
    IRoller roller,
    IDungeonNarrator narrator,
    IPartyNotifier notifier,
    ILogger<ExploreDungeonHandler> logger)
{

    private const double WoundedRatio = 0.5;

    public async Task<ExploreResult> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var (result, context, snapshot) = Mutate(id);
        if (result.Response is not { } response || context is null || snapshot is null)
        {
            return result;
        }

        // Narration is awaited outside the lock; it never influences game state.
        var flavor = await narrator.NarrateAsync(context, ct);
        response = response with { FlavorEvent = flavor };

        await BroadcastAsync(response, snapshot);
        return result with { Response = response };
    }

    // Outside the lock; a broadcast failure must never fail the explore request.
    // CancellationToken.None: an actor disconnect must not drop the broadcast to the rest of the party.
    // Separate try/catch per message so one failure does not skip the other.
    private async Task BroadcastAsync(ExploreResponse response, GetPartyStateResponse snapshot)
    {
        try
        {
            await notifier.TurnResolvedAsync(response, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast TurnResolved for turn {Turn} of party {PartyId}", response.Turn, response.PartyId);
        }

        try
        {
            await notifier.PartyUpdatedAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast PartyUpdated for turn {Turn} of party {PartyId}", response.Turn, response.PartyId);
        }
    }

    private (ExploreResult Result, NarrationContext? Context, GetPartyStateResponse? Snapshot) Mutate(Guid id)
    {
        var key = PartyCache.Key(id);
        if (!cache.TryGetValue(key, out Party? party) || party is null)
        {
            return (new ExploreResult(ExploreStatus.NotFound), null, null);
        }

        // Per-party lock: serializes mutation against Party.Snapshot() readers.
        lock (party.SyncRoot)
        {

            if (party.IsPartyDefeated)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null, null);
            }

            var room = roller.Next(2) == 0 ? RoomType.Combat : RoomType.Chest;
            var goldFound = room == RoomType.Chest ? 5 + party.Turn : 0;
            var turnBefore = party.Turn;

            DamageResult? hit = room == RoomType.Combat ? party.TakeDamage(2 + turnBefore % 3) : null;
            party.AdvanceTurn(goldFound);

            // Mutated in place under the lock; re-Set refreshes the sliding expiry.
            cache.Set(key, party, PartyCache.EntryOptions());

            // Touch the invite entry so its sliding expiry keeps pace with an active party.
            if (party.InviteCode.Length > 0) cache.TryGetValue(PartyCache.InviteKey(party.InviteCode), out _);

            var response = new ExploreResponse(
                party.Id, party.Turn, room, hit?.TargetName, hit?.Damage ?? 0, goldFound,
                FallbackFlavor.Pick(room, turnBefore), party.Gold, party.IsPartyDefeated);
            var wounded = party.Members.Sum(m => m.Hp) <= party.Members.Sum(m => m.MaxHp) * WoundedRatio;
            var context = new NarrationContext(room, party.Name, turnBefore, wounded);

            return (new ExploreResult(ExploreStatus.Explored, response), context, PartyMapping.ToResponse(party.Snapshot()));
        }
    }
}
