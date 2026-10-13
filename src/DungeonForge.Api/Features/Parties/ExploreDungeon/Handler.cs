using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonHandler(IMemoryCache cache, IRoller roller, IDungeonNarrator narrator)
{
    // Static: guards the cache read-modify-write regardless of handler lifetime (single-instance host).
    private static readonly Lock Gate = new();

    private const int WoundedHpThreshold = 10;

    public async Task<ExploreResult> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var (result, context) = Mutate(id);
        if (result.Response is not { } response || context is null)
        {
            return result;
        }

        // Narration is awaited outside the lock; it never influences game state.
        var flavor = await narrator.NarrateAsync(context, ct);
        return result with { Response = response with { FlavorEvent = flavor } };
    }

    private (ExploreResult Result, NarrationContext? Context) Mutate(Guid id)
    {
        var key = PartyCache.Key(id);
        lock (Gate)
        {
            if (!cache.TryGetValue(key, out Party? party) || party is null)
            {
                return (new ExploreResult(ExploreStatus.NotFound), null);
            }

            if (party.Hp <= 0)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null);
            }

            var room = roller.Next(2) == 0 ? RoomType.Combat : RoomType.Chest;
            var hpChange = room == RoomType.Combat ? -(2 + party.Turn % 3) : 0;
            var goldFound = room == RoomType.Chest ? 5 + party.Turn : 0;

            var hp = Math.Max(0, party.Hp + hpChange);
            var updated = party with { Hp = hp, Gold = party.Gold + goldFound, Turn = party.Turn + 1 };

            cache.Set(key, updated, PartyCache.EntryOptions());

            var response = new ExploreResponse(
                updated.Id, updated.Turn, room, hp - party.Hp, goldFound,
                FallbackFlavor.Pick(room, party.Turn), updated.Hp, updated.Gold);
            var context = new NarrationContext(room, party.Name, party.Turn, hp <= WoundedHpThreshold);

            return (new ExploreResult(ExploreStatus.Explored, response), context);
        }
    }
}
