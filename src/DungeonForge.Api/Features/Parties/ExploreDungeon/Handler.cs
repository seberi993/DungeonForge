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
    // Static: guards the cache read-modify-write regardless of handler lifetime (single-instance host).
    private static readonly Lock Gate = new();

    private const int WoundedHpThreshold = 10;

    public async Task<ExploreResult> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var (result, context, party) = Mutate(id);
        if (result.Response is not { } response || context is null || party is null)
        {
            return result;
        }

        // Narration is awaited outside the lock; it never influences game state.
        var flavor = await narrator.NarrateAsync(context, ct);
        response = response with { FlavorEvent = flavor };

        await BroadcastAsync(response, party);
        return result with { Response = response };
    }

    // Outside the lock; a broadcast failure must never fail the explore request.
    // CancellationToken.None: an actor disconnect must not drop the broadcast to the rest of the party.
    // Separate try/catch per message so one failure does not skip the other.
    private async Task BroadcastAsync(ExploreResponse response, Party party)
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
            await notifier.PartyUpdatedAsync(GetPartyStateHandler.ToResponse(party), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast PartyUpdated for turn {Turn} of party {PartyId}", response.Turn, response.PartyId);
        }
    }

    private (ExploreResult Result, NarrationContext? Context, Party? Party) Mutate(Guid id)
    {
        var key = PartyCache.Key(id);
        lock (Gate)
        {
            if (!cache.TryGetValue(key, out Party? party) || party is null)
            {
                return (new ExploreResult(ExploreStatus.NotFound), null, null);
            }

            if (party.Hp <= 0)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null, null);
            }

            var room = roller.Next(2) == 0 ? RoomType.Combat : RoomType.Chest;
            var hpChange = room == RoomType.Combat ? -(2 + party.Turn % 3) : 0;
            var goldFound = room == RoomType.Chest ? 5 + party.Turn : 0;

            var hp = Math.Max(0, party.Hp + hpChange);
            var updated = party with { Hp = hp, Gold = party.Gold + goldFound, Turn = party.Turn + 1 };

            cache.Set(key, updated, PartyCache.EntryOptions());

            // Touch the invite entry so its sliding expiry keeps pace with an active party.
            if (updated.InviteCode.Length > 0) cache.TryGetValue(PartyCache.InviteKey(updated.InviteCode), out _);

            var response = new ExploreResponse(
                updated.Id, updated.Turn, room, hp - party.Hp, goldFound,
                FallbackFlavor.Pick(room, party.Turn), updated.Hp, updated.Gold);
            var context = new NarrationContext(room, party.Name, party.Turn, hp <= WoundedHpThreshold);

            return (new ExploreResult(ExploreStatus.Explored, response), context, updated);
        }
    }
}
